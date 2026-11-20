namespace Wdc.Domain;

/// <summary>
/// Opens transactions that outlive the call that opened them: a remote client begins one, sends several requests, and
/// commits later. Implemented by the database; used by the REST server's coordinator.
/// </summary>
public interface IDetachedTransactionSource
{
    /// <summary>Opens a transaction on a connection of its own, not bound to the caller's flow.</summary>
    Task<IDetachedTransaction> BeginDetached(CancellationToken cancellationToken = default);
}
