using Avalonia.Threading;

namespace Wdc.Cube.Avalonia;

/// <summary>Bridges a Cube application to Avalonia.</summary>
public static class AvaloniaPresentation
{
    /// <summary>
    /// The presentation dispatcher on Avalonia's UI thread (decision D-01 allows the UI's own): presenters, flushes and
    /// views share one thread, and <c>await</c> comes back to it through Avalonia's synchronization context.
    /// </summary>
    public static PresentationDispatcher CreateDispatcher() =>
        PresentationDispatcher.FromHost(action => Dispatcher.UIThread.Post(action), Dispatcher.UIThread.CheckAccess);
}
