# 0003. One serial presentation dispatcher per application

## Status

Accepted (2026-11-18).

## Context

The presentation model of Cube assumes a single thread: presenters change scopes, react to clicks and await loads
without locks. The TypeScript implementation gets that from the JavaScript event loop; the Kotlin one confines each
application to a dispatcher with a parallelism of one. In .NET, the continuation of an `await` may run on any
thread-pool thread unless a `SynchronizationContext` brings it back, and each host (Blazor, a desktop UI thread, a
server, a test) has a different notion of "the" thread.

## Decision

- Each `CubeApplication` owns a `PresentationDispatcher`: a `SynchronizationContext` that runs one queued item at a
  time.
- Actions, navigation, scope flushes and history updates run on it. While an item runs the context is installed, so
  every `await` comes back to it. Items interleave only at `await` points.
- Presenters are written as single-threaded code, without locks.
- Asynchronous entry points (`Flip`, `KickStart`, `InvokeAsync`, actions) marshal themselves onto the dispatcher;
  synchronous members that touch navigation or scope state verify access and throw when called from outside.
- The dispatcher adapts to the host:
  - Blazor: the renderer's dispatcher (one application per circuit or per page);
  - Avalonia: the UI thread;
  - servers and tests: `SerialDispatcher`, a queue of its own; `ManualDispatcher` for tests that pump it by hand;
  - any other host with a serial context: `PresentationDispatcher.FromHost(post, checkAccess)`.
- Truly parallel work runs elsewhere (`Task.Run`, I/O) and comes back through `await`, `Post` or `InvokeAsync`.
- No `ConfigureAwait(false)` in presentation code; the analyzer rule that suggests it is disabled. Libraries below the
  presentation may use it internally.

## Alternatives considered

- **Rely on the host's context alone**: ties the presentation to where it is hosted, and leaves servers and tests
  without serialization.
- **No serialization, locks in presenters**: contradicts the model and pushes concurrency onto every application
  author.

## Consequences

- A whole class of races (a click against a load that finishes) disappears from application code.
- Long CPU work on the dispatcher blocks the whole application; it must be moved off explicitly.
- An exception from a posted item that nobody awaited is surfaced (`UnhandledException` → `Unexpected`), never
  swallowed.

See [Dispatcher](../concepts/dispatcher.md).
