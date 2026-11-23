namespace Wdc.Rest.Server;

/// <summary>Limits and timeouts of remote transactions.</summary>
public sealed class RemoteTransactionOptions
{
    /// <summary>Open at once, in total. Each one holds a connection.</summary>
    public int MaxOpen { get; set; } = 128;

    /// <summary>Open at once by one user, on all its clients (by one client, when anonymous).</summary>
    public int MaxOpenPerOwner { get; set; } = 16;

    /// <summary>Rolled back after this long without a request.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Rolled back after this long, used or not.</summary>
    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the outcome of a finished transaction is remembered (a repeated commit, a status query).</summary>
    public TimeSpan OutcomeRetention { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the reaper looks.</summary>
    public TimeSpan ReapInterval { get; set; } = TimeSpan.FromSeconds(5);
}
