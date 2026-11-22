# 0013. Writes in cascade, guided by the projection

## Status

Accepted (2026-11-18).

## Context

Previously, cascades were written by hand in each repository (a purchase inserting its items, deleting them before
itself), and a generic update wrote only the entity's own table: a reference changed only its foreign key, a
collection nothing. A projection already says which fields a read brings back; it can equally say which fields a write
touches, references and collections included. Whatever rules apply to a child written directly must also apply when it
is written in cascade.

## Decision

- **A write follows its projection.** If the projection marks a field, the write handles it, even when it is a 1:1
  reference or a 1:N collection. Everything runs in one transaction (the open one, or an implicit `Required`).
- **Collection marked on update = full state.** A child without a key is inserted; a child with a key is updated in the
  fields the collection's shape marks; a stored child missing from the list is deleted as an orphan (with its own
  cascade). An empty list deletes all children.
- **Delete in cascade is declared on the relation** (`Collection(..., cascadeDelete: true)`): deleting the parent first
  deletes those children, in the same transaction. Without it the collection is a view and is never deleted.
  References are never deleted in cascade.
- **Reference (foreign key in the parent):** marked only by its key, it changes the foreign key. Marked beyond the key,
  it also writes the referenced row in those fields; without a key, the referenced row is inserted first and the
  foreign key points at it.
- **Without a projection:** insert writes collections that hold values and references only by foreign key; update
  writes only the entity's own table.
- **A new child is inserted with every field that holds a value**, including the foreign key to the parent and what the
  child's rules completed; the shape decides only what to update on existing children.
- The child's foreign key comes from the child's reference mapped on the same column (`item.purchase` on
  `purchase_id`); the engine points it at the parent.
- **Children are written through their own repository** (each repository registers with its `Database`), so its
  overrides apply in cascade too.
- **A cascade guard** (`ICascadeGuard`, ambient through `CascadeGuard`) is told before each child inserted, updated or
  deleted in cascade, and may complete it or refuse by throwing. The protected repositories use it to apply each child's
  policy ([0014](0014-rules-per-role-in-protected-repository.md)), and the REST layer carries children and their
  shapes on the wire.

## Alternatives considered

- **Hand-written cascades per repository** (the earlier approach): duplicated, easy to forget, and they bypass the
  child's rules.

## Consequences

- Hand-written cascades disappear from application repositories.
- Every repository a cascade can reach must be created on the same `Database`, or the write fails naming it.
- A full-state update sent with an incomplete list deletes children; clients must send the whole collection.

See [Persistence](../concepts/persistence.md#writes-in-cascade).
