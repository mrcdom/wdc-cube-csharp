using Shopping.Domain;
using Shopping.Domain.Security;
using Shopping.Persistence;
using Shopping.Presentation.Infra;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>A Shopping database in a throwaway schema, with the demo's own DDL and data.</summary>
public class ShoppingDatabaseFixture : IAsyncLifetime
{
    public ShoppingDatabaseFixture()
        : this(DatabaseProvider.PostgreSql, "WDC_TEST_POSTGRES")
    {
    }

    protected ShoppingDatabaseFixture(DatabaseProvider provider, string variable)
    {
        Provider = provider;
        Variable = variable;
    }

    public DatabaseProvider Provider { get; }

    public string Variable { get; }

    public ShoppingStore? Store { get; private set; }

    public string SkipReason => $"{Variable} is not set (run: source scripts/test-env.sh)";

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { } connectionString)
        {
            return;
        }

        Store = ShoppingStore.Open(Provider, connectionString, "wdc_test_" + Guid.NewGuid().ToString("N")[..10]);
        await Store.EnsureCreated();
        await OnReady();
    }

    /// <summary>The master key of the users' address keys, one per fixture.</summary>
    public byte[] HistoryMaster { get; } = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    /// <summary>As Blazor Server does: repositories protected for the session's actor, set by the login.</summary>
    public virtual (ShoppingRepositories Repositories, IAuthentication Authentication) Connect()
    {
        var actors = new ActorSource();
        return (ShoppingProtection.Protect(Store!.Repositories, actors),
            new LocalAuthentication(Store.Repositories, actors, id => Domain.Users.HistoryKeys.Derive(HistoryMaster, id)));
    }

    protected virtual Task OnReady() => Task.CompletedTask;

    protected virtual Task OnDisposing() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (Store is not null)
        {
            await OnDisposing();
            await Store.Drop();
            await Store.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
