# 0014. Rules per role, in the protected repository

## Status

Accepted (2026-11-19). Amended by [0020](0020-data-policies-per-role.md): access per role can be declared as a data
policy, and the hooks below remain the alternative for an entity that needs them.

## Context

Rules that depend on who is acting (permission per operation, reach over rows, fields that must not be read, checks
on writes, the rules of children written in cascade) used to live only in the REST transport. A host that used the
repositories directly, such as Blazor Server, went through none of them: the rules held there only because the
presentation behaved. The rules of an application's roles must hold in every case and in every host, while some roles
(for example a maintenance role) may have specific business rules relaxed.

## Decision

- **Where:** a `ProtectedRepository<TEntity, TCriteria, TKey>`, in `Wdc.Domain`, wraps the raw repository and sends
  every operation through the entity's `RepositoryPolicy`. It implements the same `IRepository` contract. Every host
  (Blazor Server circuit, REST request, test) hands protected repositories to a session; the rules are declared once.
- **Who:** an `Actor` (id and roles, plain strings) read from an `IActorSource` at every call, so login and logout
  need no rebuilding. Nobody acting means `AuthenticationException` before touching the database.
- **The policy** is a set of optional delegates receiving a `PolicyContext(Actor, InCascade)`: `Authorize`, `Scope`
  (narrow the criteria with restrictions), `RestrictToId`, `SanitizeProjection`, `SanitizeOutput`, `BeforeInsert`,
  `BeforeUpdate`, `BeforeDelete`. Fields are handled by these free hooks, not by declarative per-role masks.
- **Writes** run in `Required`; a hook that throws rolls back everything, cascade children included.
- **Cascade:** during the raw write, a cascade guard ([0013](0013-writes-in-cascade.md)) finds each child's protected
  repository in the session (`Protection`) and applies *its* policy with `InCascade: true`. A child with no protected
  repository in the session is refused. The guard watches only the raw write; the rules themselves run outside it, so
  what a rule writes through the raw store stays trusted.
- **Roles belong to the application.** The framework offers the mechanism (actor, roles, policy); it has no notion of
  a super user. An application defines its roles and what each relaxes.
- **The raw repositories are the trusted inside**: seeding, authentication (which reads the user before anyone is
  acting), and the reads and corrections a policy needs. A host never hands them to a presenter.
- `RepositoryRpc` keeps only the transport: decoding, limits, encoding.

## Alternatives considered

- **Rules only in the REST server, with Blazor Server calling the API**: local would stop being local.
- **Declarative projection masks per role**: less expressive than hooks for rules that depend on data.
- **A super-user flag in the framework**: what a maintenance role may relax is a business decision.

## Consequences

- Presenters do not check permissions; they react to the exceptions the repositories raise (401, 403, 404, 400 over
  HTTP).
- The same security tests run in process and through HTTP.
- In the Shopping sample, `Super` relaxes the rules on completed purchases while every application role, including
  administrators, must go through an audited correction.

See [Protected repositories](../concepts/protected-repositories.md).
