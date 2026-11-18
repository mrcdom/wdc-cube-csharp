namespace Wdc.Cube.Blazor;

/// <summary>What <see cref="CubeHost{TApp}"/> hands to the application it creates.</summary>
/// <param name="Dispatcher">The renderer's serial context, as a presentation dispatcher.</param>
/// <param name="History">The browser's address.</param>
public sealed record CubeHostContext(PresentationDispatcher Dispatcher, HistoryManager History);
