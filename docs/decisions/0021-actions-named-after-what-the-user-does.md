# 0021. An action calls the method of its name

## Status

Accepted (2026-11-27).

## Context

A scope's actions (`OnBuy`, `OnBack`, `OnOpenProduct`) are what the view calls. The samples bound them in several
ways:
- to a method of the same name (`Scope.OnBuy = Action(OnBuy)`);
- to a method of another name (`Scope.OnConfirm = Action(Confirm)`);
- to an inline lambda, sometimes of several lines;
- to a delegate received from the owner.

Reading a presenter, the way from what the user does to the code that answers it was different each time. The
failure hook (`OnActionFailure`) names the action as it was bound, which a lambda makes hard to read. The Kotlin
implementation settled this in its ADR 0015.

## Decision

- Every action calls the presenter's method of the same name: `Scope.OnBuy = Action(OnBuy)`, also when it only
  navigates or sets a field.
- An action of a row (a line of the cart, an item of a list) passes the row, or its key, to that method:
  `row.OnRemove = Action(() => OnRemove(row))`.
- An `On…` method is an entry point from the view, and nothing else calls it. What two actions share, or what the
  presenter also does by itself, goes in a method of its own: `OnPrevious` and `OnNext` call `ShowPage`, which the
  navigation calls too.
- An action that the owner decides (a panel's "open this product") is still a method of the panel, which calls the
  delegate it was given.
- The framework does not enforce it: it is the convention of the samples and of the documentation.

## Alternatives considered

- **Names without the `On` prefix** (`Buy`): the method and the action would read differently, and the prefix is
  what tells an entry point from the view apart.
- **Lambdas for one-line navigations**: shorter, but two ways to read the same thing.
- **An analyzer that checks it**: one more Roslyn project for a rule a review catches.

## Consequences

- A presenter reads from the actions to the methods by name.
- About thirty bindings of the samples changed, with no change of behaviour; the README and the getting-started guide
  show the convention.

See [Architecture](../concepts/architecture.md#actions).
