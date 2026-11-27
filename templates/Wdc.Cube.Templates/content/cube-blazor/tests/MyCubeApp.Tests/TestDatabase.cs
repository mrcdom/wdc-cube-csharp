using MyCubeApp.Blazor.Data;
using MyCubeApp.Domain;

namespace MyCubeApp.Tests;

/// <summary>
/// The tasks in a schema of their own, created with the application's table and first tasks, and dropped at the end.
/// The server comes from <see cref="Variable"/>; without it, the test is skipped and says why.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    /// <summary>An Npgsql connection string to a database where the tests may create and drop schemas.</summary>
    public const string Variable = "TEST_POSTGRES";

    private readonly TaskStore _store;

    private TestDatabase(TaskStore store) => _store = store;

    public ITaskRepository Tasks => _store.Tasks;

    public static async Task<TestDatabase> Create()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } connectionString)
        {
            Assert.Skip($"{Variable} is not set (see README.md).");
            throw new InvalidOperationException(); // not reached: Skip throws
        }

        var store = TaskStore.Open(connectionString, "test_" + Guid.NewGuid().ToString("N")[..12]);
        await store.EnsureCreated(TestContext.Current.CancellationToken);
        return new TestDatabase(store);
    }

    public async ValueTask DisposeAsync()
    {
        await _store.Drop();
        await _store.DisposeAsync();
    }
}
