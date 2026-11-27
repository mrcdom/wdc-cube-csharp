namespace Wdc.Domain;

/// <summary>Removes a resource (DELETE): repeating it gives the same result.</summary>
// The base has no protected member to expose: deriving from a verb is how a write is declared.
#pragma warning disable RS0022
public abstract class ResourceDelete<TResult>(ITransactionService? transactions) : ResourceWrite<TResult>(transactions)
{
    private protected sealed override Task<TResult> Write(CancellationToken cancellationToken) => Delete(cancellationToken);

    protected abstract Task<TResult> Delete(CancellationToken cancellationToken);
}
#pragma warning restore RS0022
