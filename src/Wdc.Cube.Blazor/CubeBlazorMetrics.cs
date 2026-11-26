using System.Diagnostics.Metrics;

namespace Wdc.Cube.Blazor;

/// <summary>
/// The instruments of the Blazor host, on the meter <see cref="MeterName"/>:
/// <c>wdc.cube.applications.live</c>, the applications alive now (one per circuit on Server, one per page on WebAssembly).
/// </summary>
public static class CubeBlazorMetrics
{
    public const string MeterName = "Wdc.Cube.Blazor";

    private static readonly Meter s_meter = new(MeterName);

    internal static readonly UpDownCounter<long> LiveApplications =
        s_meter.CreateUpDownCounter<long>("wdc.cube.applications.live", "{application}", "Cube applications alive now.");
}
