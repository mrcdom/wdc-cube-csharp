namespace Wdc.Domain;

/// <summary>Writes a resource whole (PUT): repeating it gives the same result.</summary>
// The base has no protected member to expose: deriving from a verb is how a write is declared.
#pragma warning disable RS0022
public abstract class ResourceSave<TResult>(ITransactionService? transactions) : ResourceWrite<TResult>(transactions)
{
    private protected sealed override Task<TResult> Write(CancellationToken cancellationToken) => Save(cancellationToken);

    protected abstract Task<TResult> Save(CancellationToken cancellationToken);
}
#pragma warning restore RS0022
