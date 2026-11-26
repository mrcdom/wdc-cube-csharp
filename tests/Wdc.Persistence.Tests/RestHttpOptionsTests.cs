using System.Diagnostics.Metrics;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wdc.Domain;
using Wdc.Rest;
using Wdc.Rest.Client;
using Wdc.Rest.Server;

namespace Wdc.Persistence.Tests;

/// <summary>What every REST answer carries (headers, Server-Timing), which answer an exception becomes, and 429s.</summary>
public sealed class RestHttpOptionsTests : IAsyncDisposable
{
    private WebApplication? _app;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task<HttpClient> Start(Action<RestServerOptions>? configure = null, Action<WebApplicationBuilder>? build = null,
        Action<WebApplication>? map = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDetachedTransactionSource>(new FakeSource());
        builder.Services.AddCubeRest(configure, configureTransactions: o => o.MaxOpen = 1);
        build?.Invoke(builder);
        _app = builder.Build();
        map?.Invoke(_app);
        _app.MapGet("/ok", One);
        _app.MapGet("/cached", c =>
        {
            c.Response.Headers.CacheControl = "max-age=60";
            return One(c);
        });
        _app.MapGet("/denied", c => RestHttp.Respond(c, () => throw new AccessDeniedException("No.")));
        _app.MapGet("/timed", Timed);
        _app.MapGet("/throw/{kind}", c => RestHttp.Respond(c, () => throw ((string)c.Request.RouteValues["kind"]! switch
        {
            "stock" => new OutOfStockException("Out of stock."),
            "stock-pass" => new OutOfStockException("pass"),
            "state" => new InvalidOperationException("Bad state."),
            "slow" => new TooManyRequestsException("Slow down.", TimeSpan.FromSeconds(2.5)),
            _ => new Exception("Boom."),
        })));
        _app.MapRemoteTransactions();
        await _app.StartAsync(Ct);
        return _app.GetTestClient();
    }

    [Fact]
    public async Task Headers_SecurityAndNoStore_OnAnswersAndErrors_TheRoutesCacheControlKept()
    {
        using var http = await Start();

        foreach (var path in new[] { "/ok", "/denied" })
        {
            using var response = await http.GetAsync(path, Ct);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
            Assert.True(response.Headers.CacheControl!.NoStore, path);
            Assert.False(response.Headers.Contains("Server-Timing"));
        }

        using var cached = await http.GetAsync("/cached", Ct);
        Assert.Equal(TimeSpan.FromSeconds(60), cached.Headers.CacheControl!.MaxAge);
        Assert.False(cached.Headers.CacheControl.NoStore);
    }

    [Fact]
    public async Task Headers_CanBeChangedOrTurnedOff()
    {
        using var http = await Start(o =>
        {
            o.CacheControl = null;
            o.SecurityHeaders.Remove("X-Frame-Options");
            o.SecurityHeaders["Referrer-Policy"] = "no-referrer";
        });

        using var response = await http.GetAsync("/ok", Ct);
        Assert.Null(response.Headers.CacheControl);
        Assert.False(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task ServerTiming_TheMeasuresAndTheTotal_DescriptionsOnlyWhenAsked_AlsoOnErrors()
    {
        using var http = await Start(o => o.ServerTiming = true);

        using var timed = await http.GetAsync("/timed", Ct);
        Assert.Matches(@"^db;dur=\d+\.\d, total;dur=\d+\.\d$", timed.Headers.GetValues("Server-Timing").Single());
        using var failed = await http.GetAsync("/denied", Ct);
        Assert.Matches(@"^total;dur=\d+\.\d$", failed.Headers.GetValues("Server-Timing").Single());

        await _app!.DisposeAsync();
        using var described = await Start(o => (o.ServerTiming, o.ServerTimingDescriptions) = (true, true));
        using var withDescription = await described.GetAsync("/timed", Ct);
        Assert.Matches(@"^db;desc=""Query"";dur=\d+\.\d, total;dur=\d+\.\d$", withDescription.Headers.GetValues("Server-Timing").Single());
    }

    [Fact]
    public void ServerTiming_ANameThatIsNotAToken_IsRefused()
    {
        var context = new DefaultHttpContext();
        Assert.Throws<ArgumentException>(() => RestTiming.Add(context, "db query", TimeSpan.Zero));
    }

    [Fact]
    public async Task Errors_MostSpecificMapFirst_NullPassesOn_ThenTheContractsTable()
    {
        using var http = await Start(o => o.Errors
            .Map<InvalidOperationException>(HttpStatusCode.UnprocessableEntity)
            .Map<OutOfStockException>(e => e.Message == "pass"
                ? null
                : new RestError(HttpStatusCode.Conflict, e.Message) { Headers = new Dictionary<string, string> { ["X-Reason"] = "stock" } }));

        using var stock = await http.GetAsync("/throw/stock", Ct);
        Assert.Equal(HttpStatusCode.Conflict, stock.StatusCode);
        Assert.Equal("stock", stock.Headers.GetValues("X-Reason").Single());
        Assert.Equal("Out of stock.", RestErrors.MessageOf(await stock.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal("nosniff", stock.Headers.GetValues("X-Content-Type-Options").Single());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.GetAsync("/throw/stock-pass", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.GetAsync("/throw/state", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/denied", Ct)).StatusCode);
        using var boom = await http.GetAsync("/throw/boom", Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, boom.StatusCode);
        Assert.Equal("Internal server error", RestErrors.MessageOf(await boom.Content.ReadAsByteArrayAsync(Ct)));
    }

    [Fact]
    public async Task TooManyRequests_SendsRetryAfterInWholeSeconds_AndTheClientReadsIt()
    {
        using var http = await Start();

        using var response = await http.GetAsync("/throw/slow", Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(3), response.Headers.RetryAfter!.Delta);

        var client = new RestClient(_app!.GetTestClient());
        var refused = await Assert.ThrowsAsync<TooManyRequestsException>(() => client.Get("/throw/slow", Ct));
        Assert.Equal("Slow down.", refused.Message);
        Assert.Equal(TimeSpan.FromSeconds(3), refused.RetryAfter);
    }

    [Fact]
    public async Task RateLimiter_ItsRefusal_IsTheContractsAnswer()
    {
        using var http = await Start(
            build: b => b.Services.AddRateLimiter(o =>
            {
                o.OnRejected = RestRateLimiting.OnRejected;
                o.AddPolicy("rest", _ => RateLimitPartition.GetFixedWindowLimiter("all",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }));
            }),
            map: a =>
            {
                a.UseRateLimiter();
                a.MapGet("/limited", One)
                    .RequireRateLimiting("rest");
            });

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/limited", Ct)).StatusCode);
        using var refused = await http.GetAsync("/limited", Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.InRange(refused.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        Assert.Equal("Too many requests: try again later.", RestErrors.MessageOf(await refused.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal("nosniff", refused.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task RemoteTx_TheLimitOverTheWire_IsStillTheTransactionLimit()
    {
        using var http = await Start();
        var transactions = new RemoteTransactionService(new RestClient(_app!.GetTestClient()));
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var first = transactions.Execute(Propagation.Required, async _ =>
        {
            inside.SetResult();
            await release.Task;
            return true;
        }, Ct);
        await inside.Task;

        await Assert.ThrowsAsync<TransactionLimitExceededException>(() =>
            transactions.Execute(Propagation.RequiresNew, _ => Task.FromResult(true), Ct));
        release.SetResult();
        Assert.True(await first);
    }

    [Fact]
    public async Task Metrics_RemoteTransactions_OpenNowAndByEvent()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meters = services.GetRequiredService<IMeterFactory>();
        var coordinator = new RemoteTransactionCoordinator(new FakeSource(), new RemoteTransactionOptions { MaxOpen = 2 }, meters: meters);
        var ours = meters.Create(RestServerMetrics.MeterName);
        var values = new Dictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == ours)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => values[instrument.Name] = value);
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            values[$"{instrument.Name}/{tags[0].Value}"] = value);
        listener.Start();

        var a = await coordinator.Begin("a", Ct);
        var b = await coordinator.Begin("b", Ct);
        await Assert.ThrowsAsync<TransactionLimitExceededException>(() => coordinator.Begin("c", Ct));
        await coordinator.Commit(a, "a", Ct);
        listener.RecordObservableInstruments();

        Assert.Equal(1, values["wdc.remote.transactions.open"]);
        Assert.Equal(2, values["wdc.remote.transactions/begun"]);
        Assert.Equal(1, values["wdc.remote.transactions/committed"]);
        Assert.Equal(0, values["wdc.remote.transactions/rolledback"]);
        Assert.Equal(1, values["wdc.remote.transactions/refused"]);
        await coordinator.Rollback(b, "b", Ct);
        await coordinator.DisposeAsync();
    }

    private static Task One(HttpContext context) => RestHttp.Respond(context, OneBody);

    private static Task Timed(HttpContext context) =>
        RestHttp.Respond(context, () => RestTiming.Measure(context, "db", OneBody, "Query"));

    private static Task<byte[]?> OneBody() => Task.FromResult<byte[]?>(RestJson.Write(w => w.WriteNumberValue(1)));

    private sealed class OutOfStockException(string message) : InvalidOperationException(message);

    private sealed class FakeSource : IDetachedTransactionSource
    {
        public Task<IDetachedTransaction> BeginDetached(CancellationToken cancellationToken = default) =>
            Task.FromResult<IDetachedTransaction>(new FakeTransaction());
    }

    private sealed class FakeTransaction : IDetachedTransaction
    {
        public bool IsRollbackOnly { get; private set; }

        public Task<T> Run<T>(Func<Task<T>> work) => work();

        public void SetRollbackOnly() => IsRollbackOnly = true;

        public Task<bool> Commit(CancellationToken cancellationToken = default) => Task.FromResult(!IsRollbackOnly);

        public Task Rollback(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
