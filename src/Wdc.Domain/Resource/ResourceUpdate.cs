namespace Wdc.Domain;

/// <summary>Changes part of a resource (PATCH).</summary>
// The base has no protected member to expose: deriving from a verb is how a write is declared.
#pragma warning disable RS0022
public abstract class ResourceUpdate<TResult>(ITransactionService? transactions) : ResourceWrite<TResult>(transactions)
{
    private protected sealed override Task<TResult> Write(CancellationToken cancellationToken) => Update(cancellationToken);

    protected abstract Task<TResult> Update(CancellationToken cancellationToken);
}
#pragma warning restore RS0022
