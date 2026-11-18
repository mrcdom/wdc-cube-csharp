namespace Wdc.Domain;

/// <summary>How a unit of work relates to the transaction that may already be open (the JPA propagations).</summary>
public enum Propagation
{
    /// <summary>Joins the open transaction, or opens one.</summary>
    Required,

    /// <summary>Always opens its own, suspending the open one meanwhile.</summary>
    RequiresNew,

    /// <summary>Joins the open transaction; fails when there is none.</summary>
    Mandatory,

    /// <summary>Joins the open transaction, or runs without one.</summary>
    Supports,

    /// <summary>Runs without a transaction, suspending the open one meanwhile.</summary>
    NotSupported,

    /// <summary>Runs without a transaction; fails when one is open.</summary>
    Never,
}

/// <summary>The transaction a unit of work runs in.</summary>
public interface ITransactionContext
{
    /// <summary>Whether there is a physical transaction (false under Supports/NotSupported without one).</summary>
    bool IsActive { get; }

    bool IsRollbackOnly { get; }

    /// <summary>Undoes the transaction at the end, without throwing.</summary>
    void SetRollbackOnly();
}

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

/// <summary>The propagations as methods.</summary>
public static class TransactionServiceExtensions
{
    public static Task<T> Required<T>(this ITransactionService tx, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default) =>
        Run(tx, Propagation.Required, work, cancellationToken);

    public static Task Required(this ITransactionService tx, Func<ITransactionContext, Task> work,
        CancellationToken cancellationToken = default) =>
        Run(tx, Propagation.Required, work, cancellationToken);

    public static Task<T> RequiresNew<T>(this ITransactionService tx, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default) =>
        Run(tx, Propagation.RequiresNew, work, cancellationToken);

    public static Task RequiresNew(this ITransactionService tx, Func<ITransactionContext, Task> work,
        CancellationToken cancellationToken = default) =>
        Run(tx, Propagation.RequiresNew, work, cancellationToken);

    public static Task<T> Mandatory<T>(this ITransactionService tx, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default) =>
        Run(tx, Propagation.Mandatory, work, cancellationToken);

    public static Task<T> Supports<T>(this ITransactionService tx, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default) =>
        Run(tx, Propagation.Supports, work, cancellationToken);

    public static Task<T> NotSupported<T>(this ITransactionService tx, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default) =>
        Run(tx, Propagation.NotSupported, work, cancellationToken);

    public static Task<T> Never<T>(this ITransactionService tx, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default) =>
        Run(tx, Propagation.Never, work, cancellationToken);

    private static Task<T> Run<T>(ITransactionService tx, Propagation propagation, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tx);
        return tx.Execute(propagation, work, cancellationToken);
    }

    private static Task Run(ITransactionService tx, Propagation propagation, Func<ITransactionContext, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(work);
        return tx.Execute(propagation, async context =>
        {
            await work(context);
            return true;
        }, cancellationToken);
    }
}

/// <summary>The transaction infrastructure failed (begin, commit, rollback), as opposed to the work itself.</summary>
public class TransactionSystemException : Exception
{
    public TransactionSystemException()
    {
    }

    public TransactionSystemException(string message)
        : base(message)
    {
    }

    public TransactionSystemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A transaction in a state that does not allow the request: unknown, expired, already finished, or a write without
/// the transaction its owner has open. REST maps it to 409.
/// </summary>
public class TransactionConflictException : TransactionSystemException
{
    public TransactionConflictException()
    {
    }

    public TransactionConflictException(string message)
        : base(message)
    {
    }

    public TransactionConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Too many transactions open (in total or for one owner). REST maps it to 429.</summary>
public class TransactionLimitExceededException : TransactionSystemException
{
    public TransactionLimitExceededException()
    {
    }

    public TransactionLimitExceededException(string message)
        : base(message)
    {
    }

    public TransactionLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
