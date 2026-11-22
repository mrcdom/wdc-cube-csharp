# 0010. A SQL engine on its own AST over ADO.NET

## Status

Accepted (2026-11-18).

## Context

Repositories must derive every operation from a declared mapping: fetches shaped by a projection to any depth
(references and filtered, sliced collections), typed criteria, writes that follow the projection. The result of a
fetch is JSON read by the same codec as the REST layer ([0009](0009-shared-entity-model.md)), and the database is best
placed to build it. The JSON functions differ per database, and the core must not use runtime reflection.

## Decision

- A small SQL tree of its own (tables with aliases, columns, conditions, ordering, correlated subqueries, parameters),
  rendered per database by an `ISqlDialect` and executed with plain ADO.NET (`DbConnection`).
- Each fetch is **one statement** whose rows are JSON objects built by the database from the projection; references
  beyond their key and collections become correlated subqueries. No N+1, no join explosion.
- Values are always parameters.
- **Each repository owns its mapping and its criteria translation**, as the Kotlin repositories do:
  - a static `Mapping` (`EntityMapping.For(...).Key(...).Column(...).Reference(...).Collection(...)`), not a central
    class of mappings that would grow into a monolith;
  - a `Conditions(criteria, t)` function that translates each criterion explicitly, virtual ones included (as
    `EXISTS` subqueries);
  - relations point at the child repository's mapping (`() => ChildRepository.Mapping`), and a collection's filter is
    translated by the child's own `Conditions`.
- **A criterion that arrives filled and is never translated is a configuration error**, not an ignored filter:
  `FilterContext` records what was covered and the statement fails naming the rest. A forgotten restriction would
  otherwise be a data leak. A mapping without `Conditions` refuses any filter.

## Alternatives considered

- **linq2db**: its LINQ model fights the per-dialect JSON templates.
- **Dapper with SQL text**: loses the composition of subqueries.
- **Filters matched to fields by name**: hides the translation and makes a missed field silent.

## Consequences

- Mappings are written by hand; a generator from the schema could come later.
- Adding a database means implementing one dialect ([0011](0011-supported-databases.md)).
- `Database.Execute` and `Database.Scalar` remain for hand-written statements (DDL, bulk updates).

See [Persistence](../concepts/persistence.md).
