namespace Wdc.Domain;

/// <summary>The transaction a unit of work runs in.</summary>
public interface ITransactionContext
{
    /// <summary>Whether there is a physical transaction (false under Supports/NotSupported without one).</summary>
    bool IsActive { get; }

    bool IsRollbackOnly { get; }

    /// <summary>Undoes the transaction at the end, without throwing.</summary>
    void SetRollbackOnly();
}
