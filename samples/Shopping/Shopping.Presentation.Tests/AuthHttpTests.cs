using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shopping.Domain.Users;
using Shopping.Server;
using Wdc.Rest;

namespace Shopping.Presentation.Tests;

/// <summary>
/// The login routes: a challenge answers one login, a refresh token one refresh (even two at once), a logout revokes,
/// any instance over the same database answers them, and an address that asks too often waits.
/// </summary>
public sealed class AuthHttpTests(ShoppingHttpFixture db) : IClassFixture<ShoppingHttpFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Http()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        return db.Server.GetTestClient();
    }

    private static async Task<(string Nonce, string Digest)> Answer(HttpClient http, string userName, string password)
    {
        using var challenge = await http.GetAsync($"/api/auth/challenge?userName={userName}", Ct);
        var fields = await challenge.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var nonce = fields.GetProperty("nonce").GetString()!;
        var derived = PasswordHash.Derive(password, Convert.FromBase64String(fields.GetProperty("salt").GetString()!),
            fields.GetProperty("iterations").GetInt32());
        return (nonce, PasswordHash.Digest(derived, userName, nonce));
    }

    private static Task<HttpResponseMessage> Login(HttpClient http, string userName, string nonce, string digest) =>
        http.PostAsJsonAsync("/api/auth/login", new Dictionary<string, string> { ["userName"] = userName, ["nonce"] = nonce, ["digest"] = digest }, Ct);

    private static Task<HttpResponseMessage> Post(HttpClient http, string path, string refreshToken) =>
        http.PostAsJsonAsync(path, new Dictionary<string, string> { ["refreshToken"] = refreshToken }, Ct);

    private static async Task<string> RefreshTokenOf(HttpResponseMessage answer) =>
        (await answer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("refreshToken").GetString()!;

    [Fact]
    public async Task AChallenge_AnswersOneLogin()
    {
        using var http = Http();
        var (nonce, digest) = await Answer(http, "fulano", "fulano");

        using var first = await Login(http, "fulano", nonce, digest);
        using var again = await Login(http, "fulano", nonce, digest);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task ARefreshToken_IsSpentOnce_EvenByTwoAtOnce_AndTheNewOneWorks()
    {
        using var http = Http();
        var (nonce, digest) = await Answer(http, "fulano", "fulano");
        using var login = await Login(http, "fulano", nonce, digest);
        var token = await RefreshTokenOf(login);

        var answers = await Task.WhenAll(Post(http, "/api/auth/refresh", token), Post(http, "/api/auth/refresh", token));

        var winner = Assert.Single(answers, a => a.StatusCode == HttpStatusCode.OK);
        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.Unauthorized);
        using var next = await Post(http, "/api/auth/refresh", await RefreshTokenOf(winner));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        foreach (var answer in answers)
        {
            answer.Dispose();
        }
    }

    [Fact]
    public async Task ALogout_RevokesTheRefreshToken()
    {
        using var http = Http();
        var (nonce, digest) = await Answer(http, "fulano", "fulano");
        using var login = await Login(http, "fulano", nonce, digest);
        var token = await RefreshTokenOf(login);

        using var logout = await Post(http, "/api/auth/logout", token);
        using var refresh = await Post(http, "/api/auth/refresh", token);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task AnotherInstance_OverTheSameDatabase_AnswersTheChallengeAndTheRefresh()
    {
        using var http = Http();
        var options = db.Server.Services.GetRequiredService<ShoppingAuthOptions>();
        var other = new ShoppingTokens(options, db.Store!, TimeProvider.System);
        var (nonce, digest) = await Answer(http, "fulano", "fulano"); // challenged by the server

        var tokens = await other.Login("fulano", nonce, digest, Ct); // answered by the other instance
        Assert.NotNull(tokens);
        using var refresh = await Post(http, "/api/auth/refresh", tokens.RefreshToken); // refreshed by the server
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task AnAddressThatAsksTooOften_Waits()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddShoppingApi(db.Store!, new ShoppingAuthOptions { AuthRequestsPerMinute = 2 });
        await using var server = builder.Build();
        server.MapShoppingApi();
        await server.StartAsync(Ct);
        using var http = server.GetTestClient();

        for (var i = 0; i < 2; i++)
        {
            using var allowed = await http.GetAsync("/api/auth/challenge?userName=fulano", Ct);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await http.GetAsync("/api/auth/challenge?userName=fulano", Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.InRange(refused.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        Assert.Equal("Too many requests: try again later.", RestErrors.MessageOf(await refused.Content.ReadAsByteArrayAsync(Ct)));
        using var api = await http.GetAsync("/api/repo/product/1", Ct);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, api.StatusCode); // the limit is the login's only
    }
}
