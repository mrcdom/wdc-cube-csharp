namespace Wdc.Domain;

/// <summary>Creates a resource (POST).</summary>
// The base has no protected member to expose: deriving from a verb is how a write is declared.
#pragma warning disable RS0022
public abstract class ResourceCreate<TResult>(ITransactionService? transactions) : ResourceWrite<TResult>(transactions)
{
    private protected sealed override Task<TResult> Write(CancellationToken cancellationToken) => Create(cancellationToken);

    protected abstract Task<TResult> Create(CancellationToken cancellationToken);
}
#pragma warning restore RS0022
