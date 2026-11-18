namespace Wdc.Rest.Server;

/// <summary>Where a remote transaction is.</summary>
public enum RemoteTransactionStatus
{
    Unknown,
    Open,
    Committed,
    RolledBack,
}
