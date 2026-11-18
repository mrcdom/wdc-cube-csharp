using Microsoft.AspNetCore.Components;

namespace Wdc.Cube.Blazor;

/// <summary>Builds a <see cref="PresentationDispatcher"/> over Blazor's renderer dispatcher.</summary>
public static class BlazorPresentation
{
    /// <summary>
    /// The presentation runs on the renderer's own serial context (the circuit in Blazor Server, the single thread
    /// in WebAssembly), so scope changes, flushes and renders never cross threads.
    /// </summary>
    public static PresentationDispatcher CreateDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return PresentationDispatcher.FromHost(action => _ = dispatcher.InvokeAsync(action), dispatcher.CheckAccess);
    }
}
