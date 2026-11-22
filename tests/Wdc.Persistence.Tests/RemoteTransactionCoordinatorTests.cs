using Wdc.Domain;
using Wdc.Rest.Server;

namespace Wdc.Persistence.Tests;

/// <summary>The coordinator alone, over a fake transaction: ordering that a database test cannot pin down.</summary>
public sealed class RemoteTransactionCoordinatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhileARequestRuns_AnotherRequestAndACommitAreRefused_AndItStillCommitsAfter()
    {
        var source = new FakeSource();
        var coordinator = new RemoteTransactionCoordinator(source);
        var id = await coordinator.Begin("me", Ct);
        var (request, release) = await Hold(coordinator, id, source);

        await Assert.ThrowsAsync<TransactionConflictException>(() => coordinator.Run(id, "me", () => Task.FromResult(0), Ct));
        await Assert.ThrowsAsync<TransactionConflictException>(() => coordinator.Commit(id, "me", Ct));

        release.SetResult();
        Assert.False(await request); // the connection was still there
        Assert.Equal(RemoteTransactionStatus.Committed, await coordinator.Commit(id, "me", Ct));
    }

    [Fact]
    public async Task ARollbackWhileARequestRuns_HoldsAtOnce_AndTheConnectionIsRolledBackWhenTheRequestEnds()
    {
        var source = new FakeSource();
        var coordinator = new RemoteTransactionCoordinator(source);
        var id = await coordinator.Begin("me", Ct);
        var (request, release) = await Hold(coordinator, id, source);

        Assert.Equal(RemoteTransactionStatus.RolledBack, await coordinator.Rollback(id, "me", Ct));
        Assert.Equal(RemoteTransactionStatus.RolledBack, coordinator.Status(id, "me"));
        await Assert.ThrowsAsync<TransactionNotFoundException>(() => coordinator.Run(id, "me", () => Task.FromResult(0), Ct));
        Assert.False(source.Last!.Finished); // not under the request in progress

        release.SetResult();
        Assert.False(await request);
        Assert.True(source.Last.Finished);
        Assert.False(source.Last.Committed);
    }

    [Fact]
    public async Task ARequestThatFails_DoomsIt_AndTheCommitRollsBackAndIsRefused()
    {
        var source = new FakeSource();
        var coordinator = new RemoteTransactionCoordinator(source);
        var id = await coordinator.Begin(null, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.Run<int>(id, null, () => throw new InvalidOperationException("boom"), Ct));

        await Assert.ThrowsAsync<TransactionConflictException>(() => coordinator.Commit(id, null, Ct));
        await Assert.ThrowsAsync<TransactionConflictException>(() => coordinator.Commit(id, null, Ct)); // repeated: same answer
        Assert.Equal(RemoteTransactionStatus.RolledBack, coordinator.Status(id, null));
        Assert.False(source.Last!.Committed);
    }

    [Fact]
    public async Task AnUnknownTransaction_IsGone()
    {
        var coordinator = new RemoteTransactionCoordinator(new FakeSource());

        await Assert.ThrowsAsync<TransactionNotFoundException>(() => coordinator.Run(id: "nope", null, () => Task.FromResult(0), Ct));
        await Assert.ThrowsAsync<TransactionNotFoundException>(() => coordinator.Commit("nope", null, Ct));
    }

    /// <summary>A request inside <paramref name="id"/> that waits for its release; it answers whether the connection was gone.</summary>
    private static async Task<(Task<bool> Request, TaskCompletionSource Release)> Hold(RemoteTransactionCoordinator coordinator,
        string id, FakeSource source)
    {
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var request = coordinator.Run(id, "me", async () =>
        {
            inside.SetResult();
            await release.Task;
            return source.Last!.Finished;
        }, Ct);
        await inside.Task;
        return (request, release);
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

        public bool Committed { get; private set; }

        public Task<bool> Commit(CancellationToken cancellationToken = default)
        {
            Finished = true;
            Committed = !IsRollbackOnly;
            return Task.FromResult(Committed);
        }

        public Task Rollback(CancellationToken cancellationToken = default)
        {
            Finished = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
