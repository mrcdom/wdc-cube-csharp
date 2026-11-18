using Microsoft.Extensions.Hosting;

namespace Wdc.Rest.Server;

/// <summary>Runs <see cref="RemoteTransactionCoordinator.Reap"/> every <see cref="RemoteTransactionOptions.ReapInterval"/>.</summary>
public sealed class RemoteTransactionReaper(RemoteTransactionCoordinator coordinator, TimeProvider? time = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(coordinator.Options.ReapInterval, time ?? TimeProvider.System);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await coordinator.Reap(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
