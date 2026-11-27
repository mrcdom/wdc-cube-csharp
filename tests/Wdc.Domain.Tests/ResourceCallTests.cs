namespace Wdc.Domain.Tests;

/// <summary>
/// A write call runs in a Required transaction of the service it was given, or directly without one; a query never
/// opens one.
/// </summary>
public sealed class ResourceCallTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachWriteVerb_RunsInsideRequired()
    {
        var service = new RecordingTransactions();

        Assert.Equal("created", await new NoteCreate(service).Run(Ct));
        Assert.Equal("saved", await new NoteSave(service).Run(Ct));
        Assert.Equal("updated", await new NoteUpdate(service).Run(Ct));
        Assert.Equal("deleted", await new NoteDelete(service).Run(Ct));

        Assert.Equal(
            [(Propagation.Required, "created"), (Propagation.Required, "saved"), (Propagation.Required, "updated"),
             (Propagation.Required, "deleted")],
            service.Runs);
    }

    [Fact]
    public async Task AWrite_WithoutATransactionService_RunsDirectly() =>
        Assert.Equal("created", await new NoteCreate(null).Run(Ct));

    [Fact]
    public async Task AFailingWrite_FailsThroughTheTransaction()
    {
        var service = new RecordingTransactions();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new Failing(service).Run(Ct));

        Assert.Single(service.Failures);
    }

    [Fact]
    public async Task AQuery_OpensNoTransaction()
    {
        var service = new RecordingTransactions();

        Assert.Equal(42, await new Query().Run(Ct));

        Assert.Empty(service.Runs);
    }

    [Fact]
    public async Task TheCancellationToken_ReachesTheVerb()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NoteCreate(new RecordingTransactions()).Run(source.Token));
    }

    private sealed class NoteCreate(ITransactionService? tx) : ResourceCreate<string>(tx)
    {
        protected override Task<string> Create(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("created");
        }
    }

    private sealed class NoteSave(ITransactionService tx) : ResourceSave<string>(tx)
    {
        protected override Task<string> Save(CancellationToken cancellationToken) => Task.FromResult("saved");
    }

    private sealed class NoteUpdate(ITransactionService tx) : ResourceUpdate<string>(tx)
    {
        protected override Task<string> Update(CancellationToken cancellationToken) => Task.FromResult("updated");
    }

    private sealed class NoteDelete(ITransactionService tx) : ResourceDelete<string>(tx)
    {
        protected override Task<string> Delete(CancellationToken cancellationToken) => Task.FromResult("deleted");
    }

    private sealed class Failing(ITransactionService tx) : ResourceCreate<int>(tx)
    {
        protected override Task<int> Create(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("refused");
    }

    private sealed class Query : ResourceQuery<int>
    {
        public override Task<int> Run(CancellationToken cancellationToken = default) => Task.FromResult(42);
    }

    /// <summary>Runs the work at once and records the propagation and the result (or the failure).</summary>
    private sealed class RecordingTransactions : ITransactionService
    {
        public List<(Propagation, object?)> Runs { get; } = [];

        public List<Exception> Failures { get; } = [];

        public async Task<T> Execute<T>(Propagation propagation, Func<ITransactionContext, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await work(null!);
                Runs.Add((propagation, result));
                return result;
            }
            catch (Exception failure)
            {
                Failures.Add(failure);
                throw;
            }
        }

        public Task AfterCommit(Func<Task> task) => task();
    }
}
