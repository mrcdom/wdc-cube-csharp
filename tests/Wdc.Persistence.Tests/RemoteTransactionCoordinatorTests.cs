using Wdc.Domain;
using Wdc.Rest.Server;

namespace Wdc.Persistence.Tests;

/// <summary>The coordinator alone, over a fake transaction: ordering that a database test cannot pin down.</summary>
public sealed class RemoteTransactionCoordinatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Commit_WaitsForTheRequestInProgress()
    {
        var source = new FakeSource();
        var coordinator = new RemoteTransactionCoordinator(source);
        var id = await coordinator.Begin("me", Ct);
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var request = coordinator.Run(id, "me", async () =>
        {
            inside.SetResult();
            await release.Task;
            return source.Last!.Finished; // the connection must still be there
        }, Ct);
        await inside.Task;

        var commit = coordinator.Commit(id, "me", Ct);
        await Task.Delay(50, Ct);
        Assert.False(commit.IsCompleted);

        release.SetResult();
        Assert.False(await request);
        Assert.Equal(RemoteTransactionStatus.Committed, await commit);
    }

    [Fact]
    public async Task ARequestThatFails_MarksItRollbackOnly_AndTheCommitSaysRolledBack()
    {
        var source = new FakeSource();
        var coordinator = new RemoteTransactionCoordinator(source);
        var id = await coordinator.Begin(null, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.Run<int>(id, null, () => throw new InvalidOperationException("boom"), Ct));

        Assert.Equal(RemoteTransactionStatus.RolledBack, await coordinator.Commit(id, null, Ct));
        Assert.Equal(RemoteTransactionStatus.RolledBack, await coordinator.Commit(id, null, Ct)); // repeated: same answer
    }

    private sealed class FakeSource : IDetachedTransactionSource
    {
        public FakeTransaction? Last { get; private set; }

        public Task<IDetachedTransaction> BeginDetached(CancellationToken cancellationToken = default)
        {
            Last = new FakeTransaction();
            return Task.FromResult<IDetachedTransaction>(Last);
        }
    }

    private sealed class FakeTransaction : IDetachedTransaction
    {
        public bool Finished { get; private set; }

        public bool IsRollbackOnly { get; private set; }

        public Task<T> Run<T>(Func<Task<T>> work) => work();

        public void SetRollbackOnly() => IsRollbackOnly = true;

        public Task<bool> Commit(CancellationToken cancellationToken = default)
        {
            Finished = true;
            return Task.FromResult(!IsRollbackOnly);
        }

        public Task Rollback(CancellationToken cancellationToken = default)
        {
            Finished = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
