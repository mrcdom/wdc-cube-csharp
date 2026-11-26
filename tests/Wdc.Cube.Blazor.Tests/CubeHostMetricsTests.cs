using System.Diagnostics.Metrics;
using Bunit;

namespace Wdc.Cube.Blazor.Tests;

/// <summary>The host counts the applications alive: one more when it creates one, one less when it is disposed.</summary>
public sealed class CubeHostMetricsTests
{
    [Fact]
    public async Task LiveApplications_UpOnCreate_DownOnDispose()
    {
        long live = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == CubeBlazorMetrics.MeterName && instrument.Name == "wdc.cube.applications.live")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref live, value));
        listener.Start();

        var ctx = new BunitContext();
        ctx.Render<CubeHost<CubeApplication>>(p => p.Add(x => x.CreateApplication,
            host => new CubeApplication(host.History, [], dispatcher: host.Dispatcher)));
        Assert.Equal(1, Interlocked.Read(ref live));

        await ctx.DisposeAsync();
        Assert.Equal(0, Interlocked.Read(ref live));
    }
}
