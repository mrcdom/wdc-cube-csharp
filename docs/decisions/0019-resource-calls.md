# 0019. The presentation reaches the data through resource calls

## Status

Accepted (2026-11-27).

## Context

Presenters called the repositories directly: they built criteria and projections inline, mixed with drawing the
scope, and each write relied on the repository to be atomic. A use case that writes twice, or a write that must
join a read, had to remember to open a transaction. The Kotlin implementation solved the same problem with resource
calls.

## Decision

`Wdc.Domain` has `ResourceCall<T>` with `Run(CancellationToken)`, and the verbs:
- `ResourceQuery<T>` for reads, with no transaction of its own;
- `ResourceCreate<T>`, `ResourceSave<T>`, `ResourceUpdate<T>` and `ResourceDelete<T>` for writes. `Run` runs the
  verb's method in a `Required` transaction of the `ITransactionService` given to the constructor, or directly when
  it is `null`.

`ResourceWrite<T>` can only be extended by those four verbs (its constructor and its abstract member are
`private protected`), so every write runs in a transaction. Each call lives in a `Calls/` folder next to its
presenter. The transaction service comes through the constructor; .NET has no service locator to read it from, unlike
the Kotlin implementation's `BEAN`s.

## Alternatives considered

- **Repositories called from the presenters**, as before: the presenter mixes data access with drawing, and a write
  of several steps depends on the presenter opening the transaction.
- **Services in the presentation**, grouping calls: they group by entity rather than by operation, and leave the
  transaction to each method.

## Consequences

- A presenter reads as what it shows: `await new ReceiptQuery(App.Repositories, purchaseId: id, userId: me).Run()`.
- Over HTTP, a write call opens a remote transaction: a `begin` and a `commit` around the write.
- The calls are tested through the presenters, locally and over HTTP, with no change.

See [Transactions](../concepts/transactions.md#resource-calls).
