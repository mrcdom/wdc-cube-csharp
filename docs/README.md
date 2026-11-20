# Documentation

Start with [Getting started](getting-started.md): a small app, built step by step, with every piece of Cube in it.

## Concepts

How each part works and why. Read them in order the first time; later, go straight to the one you need.

**Presentation**

| Document | What it explains |
|---|---|
| [Architecture](concepts/architecture.md) | Places and addresses, FlipIntent, presenters, scopes, slots, keys, actions. |
| [Update cycle](concepts/update-cycle.md) | How a scope change reaches the screen, and when a scope counts as drawn. |
| [Dispatcher](concepts/dispatcher.md) | One presentation thread per application, and what that asks of your code. |
| [Navigation](concepts/navigation.md) | What a navigation guarantees when presenters refuse, redirect or fail. |
| [History](concepts/history.md) | Addresses, Back and Forward, and sealed addresses. |

**Data**

| Document | What it explains |
|---|---|
| [Domain](concepts/domain.md) | Entity models, projections, criteria, the repository contract. |
| [Persistence](concepts/persistence.md) | SQL repositories from mappings, one statement per fetch, writes in cascade. |
| [Transactions](concepts/transactions.md) | The ambient transaction, propagations, and transactions held across requests. |
| [Protected repositories](concepts/protected-repositories.md) | Rules per role, enforced in the repository every host hands to a session. |
| [Remote](concepts/remote.md) | The same contracts over HTTP: server, client, and what the presentation sees. |

## Samples

- [`samples/GettingStarted`](../samples/GettingStarted): the app of the tutorial.
- [`samples/Tutorial`](../samples/Tutorial/README.md): a page frame, a list with a dialog, TodoMVC.
- [`samples/Shopping`](../samples/Shopping/README.md): a complete application, from the database to three hosts.

## About this documentation

The code blocks marked as snippets come from `samples/GettingStarted`, and a test fails when a document and the code
differ. After changing the sample, run `python3 scripts/update-doc-snippets.py` to rewrite them.
