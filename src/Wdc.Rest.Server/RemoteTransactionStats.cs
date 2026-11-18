namespace Wdc.Rest.Server;

/// <summary>Counters of the coordinator.</summary>
public readonly record struct RemoteTransactionStats(int OpenNow, long Begun, long Committed, long RolledBack, long Reaped, long RejectedByLimit);
