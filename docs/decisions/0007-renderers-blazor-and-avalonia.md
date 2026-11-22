# 0007. Renderers: Blazor first, Avalonia second

## Status

Accepted (2026-11-18).

## Context

Presenters and scopes must not reference any rendering technology. That claim only holds if at least two renderers of
different nature run the same presentation code. The first renderer should also cover the "remote view" case that the
Kotlin implementation solves with a protocol of its own: presenters running on a server, the screen in a browser.

## Decision

**Blazor is the first renderer** (`Wdc.Cube.Blazor`), Server and WebAssembly.

- Blazor Server already runs presenters on the server and draws in the browser, which covers the remote view without a
  custom protocol.
- `CubeHost<TApp>` builds the application with a dispatcher on the renderer's dispatcher: one application per circuit
  (Server) or per page (WebAssembly).
- `ViewRegistry` maps scope types to components, `ViewSlot` renders whatever scope a slot holds, and `ScopeView`
  attaches to its scope and confirms the render in `OnAfterRender`. `NavigationHistoryManager` binds the history to the
  browser's address.

**Avalonia is the second renderer** (`Wdc.Cube.Avalonia`), as a desktop application talking to the server over HTTP.

- `AvaloniaPresentation.CreateDispatcher()` runs the presentation on Avalonia's UI thread.
- `ScopeViewLocator` is an `IDataTemplate` per scope type (a new scope instance gets a new control); views use compiled
  bindings (`x:DataType`, no reflection) on the scope, which raises `PropertyChanged` at the flush.
- `ScopeView` confirms the render from a background-priority post, after the frame's layout and render passes.
- To support binding renderers, `ScopeList` is a read-only `IList` that raises `CollectionChanged` (reset) at the flush
  (Avalonia's items controls only track change notifications from `IList` collections), and the core gained
  `MemoryHistoryManager` for hosts without an address bar.

## Alternatives considered

- **Blazor WebAssembly as the second renderer**: the same components, so it does not prove renderer independence.
- **MAUI**: heavier setup for a desktop target.
- **A custom remote-view protocol**: unnecessary while Blazor Server provides one.

## Consequences

- The same presentation project runs in Blazor Server, Blazor WebAssembly and Avalonia; the Shopping sample does so.
- Each renderer bridge implements the render handshake ([0006](0006-scope-render-handshake.md)); without
  `MarkRendered` the desktop tests hang.
- Desktop views are tested with Avalonia's headless mode, which can also capture frames.

See [Architecture](../concepts/architecture.md), [Update cycle](../concepts/update-cycle.md) and
[Dispatcher](../concepts/dispatcher.md).
