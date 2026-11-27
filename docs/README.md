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

## Guides

How to do a given task, step by step.

| Guide | What it shows |
|---|---|
| [Testing presenters](guides/testing-presenters.md) | Driving an application in tests, with no view, locally and through HTTP. |
| [Blazor](guides/blazor.md) | Hosting a Cube application in Blazor Server and WebAssembly. |
| [Avalonia](guides/avalonia.md) | Hosting a Cube application on the desktop, and testing it headless. |
| [SQL repositories](guides/sql-repositories.md) | Writing a repository for a new entity, on PostgreSQL or SQL Server. |
| [REST](guides/rest.md) | Serving repositories and transactions over HTTP, and calling them. |
| [Roles and rules](guides/roles-and-rules.md) | Writing the data policy of an entity (rows, fields, writes per role) and keeping the business rules apart. |
| [Sealed addresses](guides/sealed-addresses.md) | Encrypting the addresses, with a key per user. |
| [Several instances](guides/several-instances.md) | What instances share, routing, health checks and metrics, with a compose setup. |
| [Trimming and AOT](guides/trimming-and-aot.md) | What the libraries guarantee and what an application must do. |

## Reference

- [Wire protocol](reference/wire-protocol.md): routes, JSON of entities, projections and criteria, errors, remote
  transactions and the sealed-address envelope; enough to write a compatible client or server.
- [Decisions](decisions/README.md): the structural decisions of the framework, with their context and alternatives.

## Samples

- [A walk through Shopping](samples/shopping.md): one feature followed from the view to the database.
- [`samples/GettingStarted`](https://github.com/mrcdom/wdc-cube-csharp/tree/main/samples/GettingStarted): the app of the tutorial.
- [`samples/Tutorial`](https://github.com/mrcdom/wdc-cube-csharp/blob/main/samples/Tutorial/README.md): a page frame, a list with a dialog, TodoMVC.
- [`samples/Shopping`](https://github.com/mrcdom/wdc-cube-csharp/blob/main/samples/Shopping/README.md): a complete application, from the database to three hosts.

## About this documentation

The code blocks marked as snippets come from `samples/GettingStarted`, and a test fails when a document and the code
differ. After changing the sample, run `python3 scripts/update-doc-snippets.py` to rewrite them.
