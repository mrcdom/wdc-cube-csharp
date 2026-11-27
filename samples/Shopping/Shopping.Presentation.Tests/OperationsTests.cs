using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shopping.Domain;
using Shopping.Domain.Security;
using Shopping.Hosting;
using Shopping.Persistence;
using Shopping.Server;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>
/// What a load balancer and the metrics see: live while the process serves, ready while the database answers (with
/// the API's authentication in front, as in the server), and sign-ins counted by outcome.
/// </summary>
public sealed class OperationsTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<WebApplication> Serve(ShoppingStore store)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddShoppingApi(store, new ShoppingAuthOptions { AuthRequestsPerMinute = 0 });
        builder.AddShoppingOperations(store);
        var app = builder.Build();
        app.MapShoppingHealth();
        app.MapShoppingApi();
        await app.StartAsync(Ct);
        return app;
    }

    [Fact]
    public async Task WithTheDatabase_LiveAndReady_WithoutSigningIn()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        await using var app = await Serve(db.Store!);
        var http = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(ShoppingOperations.LivePath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(ShoppingOperations.ReadyPath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/repo/user/count", Ct)).StatusCode);
    }

    [Fact]
    public async Task WithoutTheDatabase_LiveButNotReady()
    {
        // Nothing listens on port 1: the connection is refused at once.
        await using var store = ShoppingStore.Open(DatabaseProvider.PostgreSql, "Host=127.0.0.1;Port=1;Timeout=2", "nowhere");
        await using var app = await Serve(store);
        var http = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(ShoppingOperations.LivePath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync(ShoppingOperations.ReadyPath, Ct)).StatusCode);
    }

    [Fact]
    public async Task SignIns_AreCountedByOutcome()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var outcomes = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ShoppingMetrics.MeterName && instrument.Name == "shopping.signins")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    outcomes.Add((string)tag.Value!);
                }
            }
        });
        listener.Start();

        var throttle = new LoginThrottle(db.Store!.Security, TimeProvider.System) { FreeFailures = 1 };
        var userName = "counted" + Guid.NewGuid().ToString("N")[..8];
        await throttle.Check(userName, Ct);
        await throttle.Succeeded(userName, Ct);
        await throttle.Failed(userName, Ct);
        await throttle.Failed(userName, Ct);
        await Assert.ThrowsAsync<TooManyRequestsException>(() => throttle.Check(userName, Ct));

        // Other tests may sign in meanwhile: each outcome is there at least as often as here.
        Assert.True(outcomes.Count(o => o == ShoppingMetrics.Ok) >= 1);
        Assert.True(outcomes.Count(o => o == ShoppingMetrics.Refused) >= 2);
        Assert.True(outcomes.Count(o => o == ShoppingMetrics.Throttled) >= 1);
    }
}
