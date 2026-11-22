# 0009. One entity model for SQL and JSON

## Status

Accepted (2026-11-18).

## Context

In the Kotlin implementation each entity is described twice: once in the server's SQL query builder and once in a
hand-written JSON codec shared with clients, kept in step by hand. Partial updates also need to tell "field absent"
from "explicit null", which `System.Text.Json` does not model natively. The core and the domain must not use runtime
reflection (trimming and Native AOT).

## Decision

- `Wdc.Domain` declares one `EntityModel<T>` per entity: name, getter, setter and value kind of each field, plus
  references and collections, all as delegates. No columns, no reflection.
- `Wdc.Persistence` binds columns to those fields (`EntityMapping.For(model, table, alias)`), see
  [0010](0010-own-sql-ast-over-ado-net.md).
- A generic codec, `JsonEntityCodec<T>(model)`, works on `Utf8JsonReader`/`Utf8JsonWriter`:
  - writing an entity writes only the fields its projection marks;
  - reading for update returns the entity and a projection that marks every key present in the JSON, explicit nulls
    included, so absent and null stay distinct.
- Projections are instances of the entity with markers in the wanted fields; criteria are typed classes per entity.
  Both have a JSON form compatible with the Kotlin implementation (for example `{"o":"GE","v":[…]}` for a criterion).
- The same model is compiled into the server and the client.

## Alternatives considered

- **Two descriptions per entity** (the Kotlin approach): duplication that drifts.
- **Reflection-based serialization**: incompatible with trimming and AOT, and does not track absent fields.

## Consequences

- One declaration per entity serves the SQL repositories, the REST layer and the HTTP client repositories.
- Model declarations are written by hand; a generator from the entity class could come later as an option.
- Entities have all-nullable properties: `null` means absent in a result and not wanted in a projection.

See [Domain](../concepts/domain.md).
