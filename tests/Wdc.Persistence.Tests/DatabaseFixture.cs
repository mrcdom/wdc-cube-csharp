using System.Data.Common;

namespace Wdc.Persistence.Tests;

/// <summary>
/// One schema per test run, created and dropped by the fixture. Without the connection string in the
/// environment the tests are skipped, saying why.
/// </summary>
public abstract class DatabaseFixture : IAsyncLifetime
{
    protected DatabaseFixture(string variable)
    {
        Variable = variable;
        ConnectionString = Environment.GetEnvironmentVariable(variable);
        Schema = "wdc_test_" + Guid.NewGuid().ToString("N")[..10];
    }

    public string Variable { get; }

    public string? ConnectionString { get; }

    public string Schema { get; }

    public Shopping? Shopping { get; private set; }

    /// <summary>What the contract talks to: the local repositories, or remote ones (HTTP fixtures).</summary>
    public IShoppingSide? Side { get; protected set; }

    /// <summary>Whether <see cref="Side"/> goes through HTTP.</summary>
    public virtual bool IsRemote => false;

    public string SkipReason => $"{Variable} is not set (run: source scripts/test-env.sh)";

    /// <summary>
    /// Whether a read outside a transaction sees the last committed state instead of waiting for its locks (MVCC).
    /// Always in PostgreSQL; in SQL Server only with READ_COMMITTED_SNAPSHOT on.
    /// </summary>
    public virtual Task<bool> ReadsDoNotWaitForWriters() => Task.FromResult(true);

    public async ValueTask InitializeAsync()
    {
        if (ConnectionString is null)
        {
            return;
        }

        Shopping = new Shopping(await CreateDatabase());
        Side = Shopping;
        await ExecuteScript(CreateSchemaScript());
        await OnReady();
    }

    /// <summary>After the schema exists.</summary>
    protected virtual Task OnReady() => Task.CompletedTask;

    /// <summary>Before the schema is dropped.</summary>
    protected virtual Task OnDisposing() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (ConnectionString is not null)
        {
            await OnDisposing();
            await ExecuteScript(DropSchemaScript());
            await DisposeDatabase();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Empties every table, children first.</summary>
    public Task Reset() => ExecuteScript(
        [.. new[] { "purchase_item", "purchase", "user_account", "product" }.Select(t => $"DELETE FROM {Qualified(t)}")]);

    public async Task ExecuteScript(IEnumerable<string> statements)
    {
        await using var connection = await OpenConnection();
        foreach (var sql in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    protected abstract Task<Database> CreateDatabase();

    protected abstract Task DisposeDatabase();

    protected abstract Task<DbConnection> OpenConnection();

    protected abstract IEnumerable<string> CreateSchemaScript();

    protected abstract IEnumerable<string> DropSchemaScript();

    protected abstract string Qualified(string table);
}
