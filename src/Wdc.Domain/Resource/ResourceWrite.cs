namespace Wdc.Domain;

/// <summary>
/// A write: <see cref="Run"/> runs the verb's method in a <see cref="Propagation.Required"/> transaction, joining the
/// caller's when there is one. Without a transaction service (a test without persistence) it runs directly.
/// </summary>
/// <remarks>Extended only by the verbs of this assembly: <see cref="ResourceCreate{TResult}"/>,
/// <see cref="ResourceSave{TResult}"/>, <see cref="ResourceUpdate{TResult}"/> and
/// <see cref="ResourceDelete{TResult}"/>.</remarks>
public abstract class ResourceWrite<TResult> : ResourceCall<TResult>
{
    private readonly ITransactionService? _transactions;

    private protected ResourceWrite(ITransactionService? transactions) => _transactions = transactions;

    public sealed override Task<TResult> Run(CancellationToken cancellationToken = default) =>
        _transactions is null ? Write(cancellationToken) : _transactions.Required(_ => Write(cancellationToken), cancellationToken);

    private protected abstract Task<TResult> Write(CancellationToken cancellationToken);
}
