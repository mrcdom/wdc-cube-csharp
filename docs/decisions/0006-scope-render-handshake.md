# 0006. Scopes with an explicit render handshake

## Status

Accepted (2026-11-18).

## Context

A scope is the state a presenter exposes to its view. It must become clean again only once the screen shows it. If a
scope is considered clean as soon as its view is notified (fire and forget), a change made while the view is still
drawing is either lost or piled onto a view that has not finished; rendering is usually asynchronous (Blazor schedules
a render, Avalonia lays out on its next pass). The presentation, not the rendering technology, should also decide how
often views are asked to redraw.

## Decision

- A scope moves through `Clean → Dirty → Rendering → Clean` (`ScopeRenderState`).
- A change marks the scope `Dirty`; nothing is told to the view from the setter.
- Dirty scopes are flushed in windows of about 16 ms per application (`UpdateScheduler`, on the application's
  `TimeProvider`). A flush runs `OnBeforeScopeUpdate`, applies hints, raises `PropertyChanged` and calls the view's
  `Invalidate`; the scope becomes `Rendering`.
- The view confirms with `MarkRendered()`, and the scope becomes `Clean`. A change during `Rendering` is recorded
  without notifying the view again; at confirmation the scope goes back to `Dirty` and the change goes out in the next
  window.
- A technology that draws synchronously uses an adapter that confirms at once (`ScopeViews.Immediate`); a scope with no
  view attached is clean after the flush.
- A guarded action flushes when it ends, without waiting for the window.
- Observed properties are declared with the C# 14 `field` keyword and a base `Set`:
  `public string? Name { get; set => Set(ref field, value); }`. No source generator is required; one could later be
  offered as an optional shortcut.

## Alternatives considered

- **Reset on notification**: loses updates made during a render.
- **Notify on every set**: a burst of changes becomes a burst of redraws.
- **A source generator for observed properties from the start**: more tooling to maintain, and hides a one-line
  pattern.

## Consequences

- Natural back-pressure: a slow view receives fewer, larger updates; no update is lost.
- Tests can wait until a scope is really drawn.
- Every renderer bridge must signal the end of its render (Blazor in `OnAfterRender`, Avalonia after its layout and
  render passes); a bridge that forgets it leaves scopes stuck in `Rendering`.

See [Update cycle](../concepts/update-cycle.md).
