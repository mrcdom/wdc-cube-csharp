using Shopping.Domain;
using Shopping.Persistence;
using Shopping.Presentation.Infra;

namespace Shopping.Presentation.Tests;

/// <summary>A Shopping database in a throwaway schema, with the demo's own DDL and data (decision D-14).</summary>
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

    /// <summary>What a new application instance gets: the repositories and the login (local here).</summary>
    /// <summary>The master key of the users' address keys (decision D-22), one per fixture.</summary>
    public byte[] HistoryMaster { get; } = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public virtual (ShoppingRepositories Repositories, IAuthentication Authentication) Connect() =>
        (Store!.Repositories, new LocalAuthentication(Store.Repositories, id => Domain.Users.HistoryKeys.Derive(HistoryMaster, id)));

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
