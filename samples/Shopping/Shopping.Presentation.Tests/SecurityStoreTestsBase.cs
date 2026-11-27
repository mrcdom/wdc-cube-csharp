using Microsoft.Extensions.Time.Testing;
using Shopping.Domain.Security;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>
/// The login's tables on each database: a nonce or a refresh token is spent once even by callers at the same time,
/// failures count in a row, and the throttle's waits grow from them.
/// </summary>
public abstract class SecurityStoreTestsBase(ShoppingDatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset s_now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private ISecurityStore Store
    {
        get
        {
            Assert.SkipWhen(db.Store is null, db.SkipReason);
            return db.Store!.Security;
        }
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Nonce_IsSpentOnce_ByItsUserOnly_AndNotOnceExpired()
    {
        var store = Store;
        var nonce = Unique("n");
        await store.SaveNonce(nonce, "ana", s_now.AddMinutes(2), Ct);
        Assert.True(await store.SpendNonce(nonce, "ana", s_now, Ct));
        Assert.False(await store.SpendNonce(nonce, "ana", s_now, Ct));

        var other = Unique("n");
        await store.SaveNonce(other, "ana", s_now.AddMinutes(2), Ct);
        Assert.False(await store.SpendNonce(other, "bia", s_now, Ct));
        Assert.False(await store.SpendNonce(other, "ana", s_now, Ct)); // the wrong try spent it

        var old = Unique("n");
        await store.SaveNonce(old, "ana", s_now.AddMinutes(2), Ct);
        Assert.False(await store.SpendNonce(old, "ana", s_now.AddMinutes(3), Ct));
    }

    [Fact]
    public async Task RefreshToken_SpentAtOnceByMany_OnlyOneGetsTheUser()
    {
        var store = Store;
        var userId = (await db.Store!.Repositories.Users.Fetch(new Domain.Users.UserCriteria().UserName.Eq("fulano"), limit: 1,
            cancellationToken: Ct))[0].Id!.Value;
        var hash = Unique("h");
        await store.SaveRefreshToken(hash, userId, s_now.AddHours(1), Ct);

        var spends = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.SpendRefreshToken(hash, s_now, Ct), Ct)));

        Assert.Equal(userId, Assert.Single(spends, s => s is not null));
        var expired = Unique("h");
        await store.SaveRefreshToken(expired, userId, s_now.AddHours(1), Ct);
        Assert.Null(await store.SpendRefreshToken(expired, s_now.AddHours(2), Ct));
        var revoked = Unique("h");
        await store.SaveRefreshToken(revoked, userId, s_now.AddHours(1), Ct);
        await store.RevokeRefreshToken(revoked, Ct);
        Assert.Null(await store.SpendRefreshToken(revoked, s_now, Ct));
    }

    [Fact]
    public async Task Failures_CountInARow_EvenAtOnce_StartOverWhenOld_AndClear()
    {
        var store = Store;
        var key = Unique("u");
        Assert.Null(await store.FailuresOf(key, Ct));

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => store.RecordFailure(key, s_now, s_now.AddHours(-1), Ct), Ct)));
        Assert.Equal(new LoginFailures(6, s_now), await store.FailuresOf(key, Ct));

        var later = s_now.AddHours(2);
        await store.RecordFailure(key, later, later.AddHours(-1), Ct);
        Assert.Equal(1, (await store.FailuresOf(key, Ct))!.Count);

        await store.ClearFailures(key, Ct);
        Assert.Null(await store.FailuresOf(key, Ct));
    }

    [Fact]
    public async Task Sweep_DeletesWhatExpired_AndKeepsTheRest()
    {
        var store = Store;
        var (live, dead) = (Unique("n"), Unique("n"));
        await store.SaveNonce(live, "ana", s_now.AddHours(10), Ct);
        await store.SaveNonce(dead, "ana", s_now.AddMinutes(-1), Ct);
        var (recent, old) = (Unique("u"), Unique("u"));
        await store.RecordFailure(recent, s_now, s_now.AddHours(-1), Ct);
        await store.RecordFailure(old, s_now.AddHours(-3), s_now.AddHours(-4), Ct);

        await store.Sweep(s_now, s_now.AddHours(-1), Ct);

        Assert.Null(await store.FailuresOf(old, Ct));
        Assert.NotNull(await store.FailuresOf(recent, Ct));
        Assert.False(await store.SpendNonce(dead, "ana", s_now.AddMinutes(-2), Ct)); // gone, whatever the time
        Assert.True(await store.SpendNonce(live, "ana", s_now, Ct));
    }

    [Fact]
    public async Task Keys_ComeBackInTheOrderTheyWereSaved()
    {
        var store = Store;
        var before = (await store.Keys(Ct)).Count;
        await store.SaveKey("key-1", "<key id=\"1\" />", Ct);
        await store.SaveKey("key-2", "<key id=\"2\" />", Ct);

        var keys = await store.Keys(Ct);
        Assert.Equal(["<key id=\"1\" />", "<key id=\"2\" />"], keys.Skip(before));
    }

    [Fact]
    public async Task ADatabaseMadeBeforeTheLoginTables_GetsThem_AndKeepsItsData()
    {
        var store = Store;
        var shop = db.Store!;
        var schema = shop.Database.Schema!;
        foreach (var table in new[] { "data_protection_key", "login_failure", "refresh_token", "auth_nonce" })
        {
            await shop.Database.Execute(new Wdc.Persistence.SqlStatement(shop.Provider == Persistence.DatabaseProvider.PostgreSql
                ? $"DROP TABLE \"{schema}\".{table}"
                : $"DROP TABLE [{schema}].[{table}]", []), Ct);
        }

        Assert.False(await shop.EnsureCreated(Ct)); // the schema was there: no demo data again
        var nonce = Unique("n");
        await store.SaveNonce(nonce, "ana", s_now.AddMinutes(1), Ct);
        Assert.True(await store.SpendNonce(nonce, "ana", s_now, Ct));
        Assert.Equal(4, (await shop.Repositories.Products.Fetch(new Domain.Products.ProductCriteria(), cancellationToken: Ct)).Count);
    }

    [Fact]
    public async Task Throttle_FiveFailuresAreFree_ThenTheWaitDoubles_AndASuccessClears()
    {
        var time = new FakeTimeProvider(s_now);
        var throttle = new LoginThrottle(Store, time);
        var user = Unique("User ");

        for (var i = 0; i < 6; i++)
        {
            await throttle.Check(user, Ct);
            await throttle.Failed(user, Ct);
        }

        var refused = await Assert.ThrowsAsync<TooManyRequestsException>(() => throttle.Check(user.ToUpperInvariant() + "  ", Ct));
        Assert.Equal(TimeSpan.FromSeconds(30), refused.RetryAfter);

        time.Advance(TimeSpan.FromSeconds(30));
        await throttle.Check(user, Ct);
        await throttle.Failed(user, Ct);
        Assert.Equal(TimeSpan.FromSeconds(60), (await Assert.ThrowsAsync<TooManyRequestsException>(() => throttle.Check(user, Ct))).RetryAfter);

        time.Advance(TimeSpan.FromSeconds(60));
        await throttle.Succeeded(user, Ct);
        await throttle.Failed(user, Ct);
        await throttle.Check(user, Ct);
        Assert.Equal(TimeSpan.FromMinutes(15), throttle.WaitAfter(40));
    }
}
