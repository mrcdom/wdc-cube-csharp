namespace Wdc.Domain;

/// <summary>A transaction held between requests. Only one <see cref="Run{T}"/> at a time.</summary>
public interface IDetachedTransaction : IAsyncDisposable
{
    /// <summary>
    /// Runs <paramref name="work"/> with this transaction as the ambient one: repositories and
    /// <see cref="ITransactionService"/> calls inside it join it.
    /// </summary>
    Task<T> Run<T>(Func<Task<T>> work);

    bool IsRollbackOnly { get; }

    void SetRollbackOnly();

    /// <summary>Commits (or rolls back, when rollback-only) and releases the connection; then runs the after-commit tasks.</summary>
    /// <returns>Whether it committed.</returns>
    Task<bool> Commit(CancellationToken cancellationToken = default);

    /// <summary>Rolls back and releases the connection.</summary>
    Task Rollback(CancellationToken cancellationToken = default);
}
