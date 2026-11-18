namespace Wdc.Domain;

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
