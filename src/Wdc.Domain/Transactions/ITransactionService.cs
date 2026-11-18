namespace Wdc.Domain;

/// <summary>
/// Demarcates units of work. Whoever knows the unit of work demarcates it (a use case), never the repositories.
/// A normal return commits (unless rollback-only); any exception rolls back and is rethrown unchanged.
/// </summary>
public interface ITransactionService
{
    Task<T> Execute<T>(Propagation propagation, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="task"/> after the open physical transaction commits, in scheduling order, outside of it;
    /// dropped on rollback. With no transaction open it runs now, and the returned task completes when it ends. Its
    /// failure is logged and never changes the transaction's outcome.
    /// </summary>
    Task AfterCommit(Func<Task> task);
}
