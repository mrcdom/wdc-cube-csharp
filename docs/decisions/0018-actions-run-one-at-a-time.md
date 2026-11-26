# 0018. An application's actions run one at a time

## Status

Accepted (2026-11-26).

## Context

The dispatcher of an application ([0003](0003-serial-dispatcher-per-application.md)) runs one item at a time, but
items interleave at every `await`. A guarded action that waits on I/O therefore lets the next click run in the
middle of it: two clicks on "Buy" both passed the cart's checks and recorded two purchases, and an action could see
another one's state halfway. Disabling the button does not close the gap, since the view only learns of it at the
next flush.

## Decision

- `CubeApplication.RunAction` runs an action as the application's only action in progress. One that starts while
  another is running waits for it to end, without holding a thread (`SemaphoreSlim.WaitAsync`).
- Every guarded action (`Action(...)` in presenters and in the application presenter) runs through it, including
  the flush and the address update at its end.
- An action started from inside a running one, directly or in work it awaits, runs at once: the mark of the action in
  progress flows with the async context (`AsyncLocal`), so an action never waits for itself.
- A guarded action checks again, after its wait, whether its presenter is still on the face: the click that waited
  behind a "Buy" that went to the receipt does nothing.
- Navigation keeps its own rule for concurrent requests ([0005](0005-concurrent-navigation-requests.md)); unguarded
  view plumbing (`Mirror`) stays outside the lock, so typing is never held back by an action.

## Alternatives considered

- **Let actions interleave, and have each presenter guard itself** (a "busy" flag checked on entry): every presenter
  would need its own guard against double submission and torn state, and forgetting one is a bug no test notices.
- **Drop the second click while one runs**: loses actions the user meant (a second, different button).

## Consequences

- A double click makes two actions one after the other, never interleaved; when the first one leaves the face, the
  second one finds its presenter released and does nothing.
- An action must not wait for work that runs another action outside its own flow (a task started elsewhere and
  awaited here): that action waits for this one, and neither ends.

See [Dispatcher](../concepts/dispatcher.md).
