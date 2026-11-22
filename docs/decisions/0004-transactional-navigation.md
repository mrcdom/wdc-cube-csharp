# 0004. Transactional navigation and the meaning of `false`

## Status

Accepted (2026-11-18).

## Context

A navigation (a *flip*) walks the requested place from the root, reusing live presenters, creating missing ones and
applying the intent to each; any level may wait on I/O, refuse, or redirect. The two earlier implementations each got
part of this right: Kotlin has the stronger guarantees (exact release, awaited rollback, bounded redirects), TypeScript
the clearer API (a declared place tree, `FlipIntent`, scope slots, keys classes, `KickStart`, a history manager with an
optional codec). TypeScript also left the current place pointing at a requested leaf that had no presenter when a level
stopped the walk early.

## Decision

Take Kotlin's algorithm with TypeScript's API, and define the outcome of `ApplyParameters` precisely.

- **Places** form an immutable tree declared once; navigation state (live presenters, current place, flip context) is
  per application.
- **`ApplyParameters` returns:**
  - `true`: accepted; the walk goes on to the next level.
  - `false`: accepted, **but stop here**. The face is committed up to this presenter, and `LastPlace` becomes this
    presenter's place, not the leaf that was asked for. The published address is that place's.
  - **an exception**: the only way to refuse and keep the previous state. The flip rolls back and rethrows.
- **Guarantees:**
  - every presenter created gets `Release()` exactly once, leaves first;
  - presenters created by an interrupted walk carry over to the walk that replaces it;
  - rollback re-applies the source intent to the presenters that were alive before, and is awaited;
  - the walk works on a copy; live presenters change only at commit;
  - the history is republished at the end of every attempt, success or failure.
- **Redirect**: a presenter starts another navigation from its `ApplyParameters` (and usually returns `false`). It
  retargets the running attempt instead of starting a second one. Cycles are bounded: more than 5 visits to one path
  or more than 10 redirects in one attempt throw `CubeNavigationException` and roll back.

## Alternatives considered

- **TypeScript's algorithm**: simpler, but without exact-once release or awaited rollback.
- **`false` as refusal**: would make "stop at a guard and show what is there" impossible to express without an
  exception, and leaves two ways to refuse.

## Consequences

- Guards are plain presenters that redirect; every place under them is protected whatever the entry (click, link,
  Back).
- The scenarios of both earlier test suites, adjusted to this semantics, are the executable specification.
- `Flip` returns `true` only when the committed face is the place asked for.

See [Navigation](../concepts/navigation.md) and [Architecture](../concepts/architecture.md).
