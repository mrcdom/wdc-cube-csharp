namespace Wdc.Cube.Testing;

/// <summary>Helpers for presentation tests without a UI.</summary>
public static class CubeTestExtensions
{
    /// <summary>
    /// Lets queued work run and flushes the pending scope updates, repeating until nothing is pending, so assertions
    /// read settled state instead of state in transit. Never use a sleep for this.
    /// </summary>
    /// <exception cref="InvalidOperationException">Updates never settle (an update loop).</exception>
    public static async Task Settle(this CubeApplication app, int maxRounds = 20)
    {
        ArgumentNullException.ThrowIfNull(app);
        for (var round = 0; round < maxRounds; round++)
        {
            var pending = await app.InvokeAsync(() =>
            {
                app.Updates.FlushNow();
                return Task.FromResult(app.Updates.HasPendingUpdates);
            });

            await app.InvokeAsync(static () => { }); // anything queued meanwhile runs before the next look
            if (!pending && !await app.InvokeAsync(() => Task.FromResult(app.Updates.HasPendingUpdates)))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Scope updates did not settle after {maxRounds} rounds.");
    }

    /// <summary>The presenter mounted at <paramref name="place"/>, typed; fails loudly when there is none.</summary>
    public static T PresenterOf<T>(this CubeApplication app, Place place)
        where T : class, ICubePresenter
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.GetPresenter(place) as T
               ?? throw new InvalidOperationException(
                   $"No {typeof(T).Name} mounted at '{place}' (found {app.GetPresenter(place)?.GetType().Name ?? "nothing"}).");
    }
}
