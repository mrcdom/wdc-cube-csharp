# 0005. Concurrent navigation requests

## Status

Accepted (2026-11-18).

## Context

A flip can wait on I/O, so another navigation may be requested while one is running: a redirect from a presenter, a
click during a slow load, a Back press. In the earlier implementations any request made during an attempt took over
that attempt. As a result, a few clicks on the same destination during a single load could trip cycle detection, and
work left running by a finished attempt could hijack a later one.

## Decision

The rule depends on where the request comes from.

- **From inside the running attempt** (a presenter redirecting from its `ApplyParameters`): it retargets the attempt,
  as described in [0004](0004-transactional-navigation.md). This is the guard pattern.
- **From outside** (an action of a view, a history change, work started by an attempt that has already ended): it
  **waits** for the running attempt to end, then runs.
- **Only the latest waits.** A waiting request is discarded when another one arrives; its task completes with `false`.
- **If the running attempt fails**, the waiting request is discarded too.
- A waiting request is built from the state at the moment it runs, not at the moment it was made.
- A history change reads the address when it arrives (later, the running attempt would have published its own address
  over the user's Back), then waits like any other outside request.

"Inside" is tracked by an ambient flip context (`AsyncLocal`) bound at the start of the attempt, which flows through
every `await` of its `ApplyParameters` calls and nowhere else. Work that outlives the attempt carries a context that is
no longer the running one, so it counts as outside. `PresentationDispatcher.InvokeAsync` flows the caller's execution
context, so a presenter that left the dispatcher and redirects from there is still inside and does not deadlock; a plain
`Post` does not flow it.

## Alternatives considered

- **Every request takes over the running attempt** (the earlier behavior): spurious cycle errors on repeated clicks,
  and stale work can steal a navigation.

## Consequences

- Rapid clicks resolve to the last one; earlier ones complete with `false`.
- Code that fires a load with `_ = Load()` and navigates when it finishes behaves as an outside request.

See [Navigation](../concepts/navigation.md#concurrent-requests) and [Dispatcher](../concepts/dispatcher.md).
