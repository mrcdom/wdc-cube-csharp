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
