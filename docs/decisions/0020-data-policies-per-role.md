# 0020. Data policies per role, declared

## Status

Accepted (2026-11-27). Amends [0014](0014-rules-per-role-in-protected-repository.md), which rejected declarative
masks per role.

## Context

[0014](0014-rules-per-role-in-protected-repository.md) put the rules per role in the protected repository, as hooks:
free delegates for authorizing, scoping, sanitizing and checking writes. Hooks can express anything, but what most
entities need is the same few things said by hand each time: which rows each role reaches, which fields it reads,
which it writes. A field that should not be read was removed from the projection without a word, and an update out of
reach was always "not found". The Kotlin implementation has since declared this per role, field by field, with masks
for partial reads, at its REST boundary.

## Decision

- **A data policy per entity** (`DataPolicy<TEntity, TCriteria>`, in `Wdc.Domain`) declares grants per role:
  - the rows a grant reaches (`IRowScope`: a criteria to restrict a query and an in-memory check for a row);
  - the fields read as they are, the fields read through a mask (`KeepFirst`, `KeepLast`), and the fields created
    and updated;
  - whether the grant deletes.
- **Fields are named as in the entity model**, as strings checked when the policy is built. Building also fails on a
  criterion that maps to no field, and on a reference or collection without a policy.
- **Allow-wins across roles, fields per row.** A row shows what the grants that reach it allow, so two roles do not
  add up to fields over rows neither one reaches.
- **Refused, not trimmed.** Projecting, filtering or sorting by a field no grant allows is a 403 that names it. A
  criterion over another entity's field (`customerName` → `user.name`) also takes that entity's policy reading it.
  Only a read without a projection is trimmed, to the default shape the actor may read. An update out of reach is a
  404 when the actor cannot read the row, and a 403 when they can. An update that would move the row out of reach is
  refused.
- **Rows as alternative groups.** The scopes of the grants become `anyOf` groups of the criteria, each carrying the
  groups the criteria already had.
- **Applied in the protected repository, in every host.** The policy is not applied only at the REST boundary, as
  the Kotlin implementation does. A Blazor Server circuit, a REST request and a test get the same rules. Every child
  written in cascade answers to its own entity's grants:
  - inserted, it must be creatable and reached;
  - updated, its changed fields must be updatable;
  - deleted, some grant must delete it.
- **One or the other.** An entity is protected by a data policy or by the hooks of 0014, never both. Hooks stay for
  rules no declaration expresses, and a hook may use a data policy through `AccessFor`.
- **Business rules apart.** Rules that are not about who reaches what live in the repository the protected one
  wraps. Examples: a price taken from the product, a record that does not change once made, a correction applied to
  other rows. Such a layer may still know who is acting, as a maintenance role that relaxes some rules.

## Alternatives considered

- **Hooks only (0014 as it was)**: every entity says the same things by hand, and fields disappear silently.
- **The policy only at the REST boundary (as in Kotlin)**: Blazor Server would read with no rules, which 0014 already
  rejected.
- **Policy and hooks together on an entity**: two places to read to know what a role may do.
- **Typed field handles or expressions in the grants**: changes to every entity model, or metadata that weighs on
  trimming.

## Consequences

- A client that asks for a field it may not read gets a 403 instead of an empty value: asking for a password is an
  error, not a no-op.
- The Shopping sample declares the access of its six entities as data policies (`ShoppingPolicies`). Its business
  rules moved to repositories between the protected ones and the store (`ShoppingRules`).
- The REST server no longer fills a missing projection before calling the repository: the repository chooses it.

See [Protected repositories](../concepts/protected-repositories.md) and [Roles and rules](../guides/roles-and-rules.md).
