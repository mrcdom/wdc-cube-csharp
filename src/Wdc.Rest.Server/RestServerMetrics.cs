namespace Wdc.Rest.Server;

/// <summary>
/// The instruments of the REST server, on the meter <see cref="MeterName"/>:
/// <list type="bullet">
/// <item><c>wdc.remote.transactions.open</c>: remote transactions open now.</item>
/// <item><c>wdc.remote.transactions</c>: remote transactions by <c>event</c> (<c>begun</c>, <c>committed</c>,
/// <c>rolledback</c>, <c>reaped</c>, <c>refused</c>).</item>
/// </list>
/// </summary>
public static class RestServerMetrics
{
    public const string MeterName = "Wdc.Rest.Server";
}
