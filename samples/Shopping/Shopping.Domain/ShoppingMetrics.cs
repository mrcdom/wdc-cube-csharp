using System.Diagnostics.Metrics;

namespace Shopping.Domain;

/// <summary>
/// The Shopping application's own instruments, on the meter <see cref="MeterName"/>:
/// <c>shopping.signins</c> counts sign-in attempts by <c>outcome</c>: <see cref="Ok"/>, <see cref="Refused"/> (wrong
/// user name or password) and <see cref="Throttled"/> (refused unchecked, while the user name waits).
/// </summary>
public static class ShoppingMetrics
{
    public const string MeterName = "Shopping";

    public const string Ok = "ok";

    public const string Refused = "refused";

    public const string Throttled = "throttled";

    private static readonly Meter s_meter = new(MeterName);

    private static readonly Counter<long> s_signIns =
        s_meter.CreateCounter<long>("shopping.signins", "{attempt}", "Sign-in attempts, by outcome");

    public static void SignIn(string outcome) =>
        s_signIns.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
