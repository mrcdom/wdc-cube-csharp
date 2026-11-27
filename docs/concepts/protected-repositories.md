# Protected repositories

A protected repository is a repository seen by whoever is acting. It implements the same
`IRepository<TEntity, TCriteria, TKey>` contract as the repository it wraps, and sends every operation through the
entity's rules:
- who may do it;
- which rows they reach;
- which fields they read and write;
- what each child written in cascade must satisfy.

Every host hands protected repositories to a session (a Blazor Server circuit, a REST request, a test), so the rules
are declared once, in the domain, and hold the same way everywhere. Presenters do not check permissions: they use the
repositories they were given and react to the exceptions those raise.

All framework types on this page live in the `Wdc.Domain` namespace (`src/Wdc.Domain/Security`).

## Who is acting

- `Actor` is an id and a set of roles, both plain strings written by the application. The framework only compares them (`actor.HasRole("Super")`); what a role may do is the application's business.
- `IActorSource` has one member, `Actor? Current`. A protected repository asks it at *every call*, so the actor can change during a session (login, logout) without rebuilding anything.
- `ActorSource` is the settable implementation: `new ActorSource()` starts with nobody; its owner sets `Current`.
- Where the actor comes from depends on the host:
  - on a server, per request, from the request's token;
  - in Blazor Server, per circuit, set by the login and cleared by the logout;
  - in a test, by whoever builds the session.
- `null` means nobody. Nothing protected is served to nobody: every operation throws `AuthenticationException` before touching the database.

## Two kinds of rules

An entity is protected by one of two kinds of rules, never both:

- **A data policy** (`DataPolicy<TEntity, TCriteria>`), declared per role. It says which rows each grant reaches,
  which fields it reads (as they are or masked), which it creates and updates, and whether it deletes. This is the
  usual choice.
- **Hooks** (`RepositoryPolicy<TEntity, TCriteria, TKey>`), free delegates for rules no declaration expresses. A hook
  may still use a data policy, through `AccessFor(actor)`.

Each kind has its own constructor of `ProtectedRepository`. Rules that are not about who reaches what belong to the
repository the protected one wraps:
- a price taken from the product;
- a record that does not change once made.

See [the trusted inside](#the-trusted-inside-and-the-business-rules).

## Data policies

```csharp
public static DataPolicy<User, UserCriteria> Build() =>
    DataPolicy.For(User.Model, () => new UserCriteria())
        .WriteOnly("password")
        .Criterion("userId", "id")
        .Orderings(c => c.OrderBy == UserOrderBy.NameAToZ ? ["name"] : [])
        .Grant(["Super", "Admin"], g => g
            .Read("userName", "name", "roles")
            .Create("userName", "password", "name", "roles")
            .Update("userName", "password", "name", "roles")
            .Delete())
        .Grant(["Manager"], g => g.Read("userName", "name", "roles"))
        .Grant(["Customer"], g => g.Rows(OwnUser.Scope).Read("userName", "name", "roles"))
        .Build();
```

What the policy knows about the entity, beyond its model:

| Declaration | Meaning |
|---|---|
| `WriteOnly(fields)` | Written, never read by anyone: no grant may read it, and asking for it is refused even without security. |
| `Criterion(name, paths)` | The field a criterion filters when its name is not the field's (`userId` filters `id`). It can be a path into a reference (`customerName` filters `user.name`), or several fields for a search (`search` filters `name` and `description`). |
| `Orderings(criteria => fields)` | The fields each named ordering sorts by; the key need not be named. |
| `Reference(field, () => policy)`, `Collection(field, inverse, () => policy)` | The policy of the entity a reference or a collection points to. What is read inside it goes through that policy. |
| `DefaultShape(() => projection)` | What a read without a projection gets, before it is trimmed. Without it: the model's default fields, but the collections and the write-only ones. |

Each grant (`DataGrantBuilder`) declares:

| Declaration | Meaning |
|---|---|
| `Rows(scope)` | The rows the grant reaches (`IRowScope`); every row when not declared. |
| `Read(fields)` | Read as they are, and usable in filters and orderings. The key is always read. |
| `Mask(mask, fields)` | Read through `Mask.KeepFirst(n)` or `Mask.KeepLast(n)`. Text fields only, never the key, never written by the same grant. |
| `Create(fields)`, `Update(fields)` | What may be set when creating, and changed in a reachable row. The key is never written. |
| `Delete()` | The reachable rows may be deleted. |

`Build()` checks every name, and fails on:
- a criterion with no field;
- a reference or collection without a policy;
- a contradiction, such as a masked key, a write-only field that is read, or a field masked and written by the same
  grant.

So a gap fails when the policy is built, not on a request.

### Row scopes

An `IRowScope<TEntity, TCriteria>` says which rows a grant reaches, twice, and both must agree:

- `Restrict(group, actor)` fills an empty criteria, so the database returns only the reachable rows;
- `Covers(row, actor, ct)` checks a row in memory, so each returned row gets the fields of the grants that reach it.
  It also checks a row being created, and the stored row of an update;
- `Projection()` names what `Covers` reads beyond the key, which is fetched for it and cleared before the rows leave.

```csharp
internal sealed class OwnPurchases : IRowScope<Purchase, PurchaseCriteria>
{
    public static readonly OwnPurchases Scope = new();

    public void Restrict(PurchaseCriteria group, Actor actor) => group.UserId.Eq(ShoppingPolicies.IdOf(actor));

    public ValueTask<bool> Covers(Purchase row, Actor actor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(row.User?.Id == ShoppingPolicies.IdOf(actor));

    public Purchase? Projection() => new() { User = new User { Id = Mark.Int64 } };
}
```

A policy compares scopes by identity: declare each one once and share it. A `Covers` may look up what the row does
not carry (the owner of the purchase an item points to) through the trusted inside.

### How a request is checked

The model is **allow-wins**: an actor with several roles gets every grant of every role, and a request is allowed when
some grant allows it. Fields are decided **per row**: reading everyone's name in one role and one's own document in
another does not add up to everyone's document.

- **Count, Fetch, FetchPage**:
  - every projected field must be seen by some grant;
  - every field the criteria filters or sorts by, its alternative groups included, must be read as it is by one grant.
    Through a path, the other entity's policy must read it too;
  - the rows are restricted to those grants' scopes, as alternative groups of the criteria, each carrying the groups
    the criteria already had. No group is added when one of them reaches every row;
  - after the fetch, each row shows, per field, what the grants that reach it allow: as it is, masked, or nothing;
  - inside a reference or a collection, the other entity's policy decides, and restricts the rows of a collection
    too;
  - a read without a projection gets the default shape, **trimmed** to what the actor may read instead of refused.
- **FetchById**: the same read, through the criteria (`restrictToId`), so a row out of reach is not found (`null`).
- **Insert**: one grant must allow every field sent and reach the new row. A reference is written by its key only.
  Each child of a collection is checked as a new row of its entity, belonging to this one. What comes back is shaped
  as a read of what was written.
- **Update**:
  - one grant must reach the stored row, allow every changed field, and still reach the row once changed: a grant
    over one's own rows cannot hand a row to someone else;
  - a row no updating grant reaches is a `KeyNotFoundException` when the actor cannot read it either, as for a row
    that does not exist; it is an `AccessDeniedException` when they can.
- **Delete**: restricted to the rows of the grants that delete, which must read what the criteria filters by. A
  criteria with no filter is refused first.

A refused request is an `AccessDeniedException` that names the fields at fault. Refusing is deliberate: a client whose
request is quietly trimmed shows blanks without knowing why.

## Hooks

`RepositoryPolicy<TEntity, TCriteria, TKey>` is built from the entity model and a criteria factory, and every rule is
an optional delegate. One instance serves every session: the policy holds no actor, it receives one in a
`PolicyContext(Actor Actor, bool InCascade)`. `RepositoryOperation` is what `Authorize` is asked about.

| Hook | Signature (after the context) | Purpose |
|---|---|---|
| `Authorize` | `RepositoryOperation` | Throw `AccessDeniedException` unless the actor may do the operation. |
| `Scope` | `TCriteria` | Narrow a criteria to the actor's reach. Applies to reads, update and delete. |
| `RestrictToId` | `(TCriteria, TKey)` (no context) | Restrict a criteria to one key. Required when there is a `Scope` or a `BeforeDelete`. |
| `SanitizeProjection` | `TEntity` (the projection) | Remove from a requested projection what the actor may not read. |
| `SanitizeOutput` | `TEntity` (a result) | Remove from an answer what the actor may not see. |
| `BeforeInsert` | `TEntity, CancellationToken` | Check or complete an entity before it is inserted. |
| `BeforeUpdate` | `TEntity, TEntity written, CancellationToken` | Check an update; `written` marks the fields being written. |
| `BeforeDelete` | `TCriteria, CancellationToken` | Check a delete, given the criteria of the rows that would go (already within reach). |

Rules about writing a scope:

- Narrow with `Restrict`, not `Eq`: `criteria.UserId.Restrict(owner)`. A restriction is combined with AND outside whatever the caller asked on the same field, so a request such as `UserId.Or().IsNotNull()` cannot widen it.
- A policy with a `Scope` or a `BeforeDelete` must set `RestrictToId`; without it, the first operation that needs a single-key criteria throws `InvalidOperationException`.
- The scope and the sanitizers change the criteria and the projection the caller passed. Do not reuse them expecting the original values.

What each operation does with hooks (from `ProtectedRepository.cs`):

- **Count**: authorize → scope → inner count.
- **Fetch / FetchPage**: authorize → default the projection to the entity's own fields → `SanitizeProjection` → scope → inner fetch → `SanitizeOutput` on each item.
- **FetchById**: authorize → `SanitizeProjection` → if the policy has a `Scope`, the read goes *through the criteria* (`RestrictToId` + `Scope`, limit 1), so a row outside the reach is simply not found (`null`); without a scope, the inner `FetchById` → `SanitizeOutput`.
- **Insert**: authorize → in a transaction: `BeforeInsert` → inner insert → `SanitizeOutput` on the inserted bean. Insert does not apply the scope: ownership on insert is `BeforeInsert`'s job.
- **Update**: authorize → the key is required (`InvalidRequestException`) → the written marks are computed (the projection given; otherwise the fields that changed against `oldBean`; otherwise the entity's own fields) → in a transaction: the row must be within reach (else `KeyNotFoundException`) → `BeforeUpdate(context, newBean, written)` → inner update.
- **Delete**: authorize → a criteria with no filter is refused (`InvalidRequestException`), checked *before* the scope is added, since "delete everything I can reach" is not a use case → scope → in a transaction: `BeforeDelete` → inner delete.

## Transactions and errors

Writes run in `Propagation.Required` on the session's `ITransactionService`: they join a transaction already open, or
open their own. A refusal, by the policy, a hook or a business rule, rolls back everything the write did, cascade
children included.

Exceptions, and the HTTP status they become when the call goes over [REST](remote.md):

- `AuthenticationException`: nobody is acting (401).
- `AccessDeniedException`: the actor may not do it (403).
- `KeyNotFoundException`: the row is not within reach (404).
- `InvalidRequestException`: the request itself is wrong (400).

## Cascade: the child's rules apply

A write can reach other entities through its projection: a purchase inserted with its items, or a full-state update
that adds, changes or removes items. Those children are written by the wrapped repository, but they do not escape the
rules.

- `Protection` is what the protected repositories of one session share: the `IActorSource`, the
  `ITransactionService`, and a registry of the protected repositories by entity type. Each `ProtectedRepository`
  registers itself in its constructor.
- During the write of a protected write, a cascade guard (`ICascadeGuard`, carried by `CascadeGuard` in an
  `AsyncLocal`) is active. The repository reports each child it inserts, updates or deletes in cascade. The guard
  finds the child's protected repository and runs *its* rules:

  | Child | With a data policy | With hooks |
  |---|---|---|
  | Inserted | A grant creates its fields and reaches it, with its parent set. | `Authorize(Insert)`, then `BeforeInsert`, with `InCascade: true`. |
  | Updated | Its changed fields (against the stored row) need an update grant that reaches it before and after. A child sent as it is stored needs none. | `Authorize(Update)`, then within reach, then `BeforeUpdate`. |
  | Deleted (an orphan, or a cascade delete) | Some grant that deletes must reach it. | `Authorize(Delete)`, then within reach for each key, then `BeforeDelete`. |

- A child whose entity has no protected repository in the session is refused with `AccessDeniedException` ("not
  protected here"): it cannot be written through another entity.
- The entity the caller wrote directly is not reported again; its own rules already ran.
- The guard belongs to the calling flow only, and watches only the write itself. Outside a protected write, the
  wrapped repositories are free.

## The trusted inside and the business rules

The repositories of the store are the *trusted inside*. Nothing protects them, and they are used where nobody is
acting yet, or where a rule needs the truth rather than the actor's view:

- **Login.** `LocalAuthentication` reads the user (with the password hash) through the store, then sets
  `ActorSource.Current`. `ShoppingTokens` does the same on the server.
- **Seeding.** Demo data is written through the store.
- **Row checks and business rules** read, and sometimes write, through the store.

Between the protected repositories and the store, an application may wrap the store with its **business rules**:
repositories that implement the same contract and add what is not about access. They run inside the protected write's
transaction, after the policy's checks. Because a host builds them per session, they may know who is acting, for
example to relax a rule for a maintenance role.

What a session gets is always protected. A host never hands the store, or the business rules, to a presenter.

## The Shopping sample

### Who reaches what

`ShoppingPolicies` builds the data policy of each of the six entities. The policies refer to each other (a purchase
reads its items through the items' policy), so they are built together:

| Role | Reads | Writes |
|---|---|---|
| `Super` (maintenance) | everything but passwords | everything |
| `Admin` | everything but passwords | users and products; creates purchases for anyone; records errata |
| `Manager` | everything but passwords | products; records errata |
| `Customer` | the catalogue, themselves, their own purchases, items and errata (not who corrected them) | creates their own purchases with their items |

- The password is write-only: no role reads it, and asking for it is a 403.
- A customer's purchase must name the customer: one on someone else's behalf is out of their reach.
- Only `Super` updates or deletes purchases, items and errata.
- `PurchaseCriteria.CustomerName` filters `user.name`, and `ProductId` filters `items.product`. Filtering by them takes
  the user's and the item's policies reading those fields.

### The business rules

`ShoppingRules.Wrap(store, actors, time)` wraps the store with what is not about access:

- **Users.** The password is stored as its hash, whoever writes it.
- **Price.** An item takes the price of its product, never one of its own: on insert (alone or in cascade with its
  purchase) the price is read from the product, and writing an item's price is refused, maintenance included.
- **Made means made.** A purchase is made when it is created. Once made, it does not change and is not deleted, and no
  item enters, changes or leaves it.
- **Errata.** Recording a `PurchaseAmendment` is the correction:
  1. the purchase is locked;
  2. each line is checked against the current items: the amount only goes down, and never every item;
  3. the items are corrected through the store, in the insert's transaction;
  4. the record is filled from the data, with the date from the server's clock and the author from the actor.

  An errata is recorded by itself, never with its purchase. Its lines enter only with it, and neither changes nor
  leaves.
- **Maintenance.** `Permissions.Maintains(actor)` is true for `Super`. The rules marked as relaxable are relaxed for it:
  a purchase made, its items and its errata change for it. The others still hold.

### Wiring a session

```csharp
public static ShoppingRepositories Protect(ShoppingRepositories store, IActorSource actors, TimeProvider? time = null)
{
    var policies = new ShoppingPolicies(store);
    var ruled = ShoppingRules.Wrap(store, actors, time ?? TimeProvider.System);
    var protection = new Protection(actors, store.Transactions);
    return new ShoppingRepositories(
        new ProtectedUserRepository(ruled.Users, policies.Users, protection),
        new ProtectedProductRepository(ruled.Products, policies.Products, protection),
        new ProtectedPurchaseRepository(ruled.Purchases, policies.Purchases, protection),
        new ProtectedPurchaseItemRepository(ruled.PurchaseItems, policies.Items, protection),
        new ProtectedPurchaseAmendmentRepository(ruled.Amendments, policies.Amendments, protection),
        new ProtectedPurchaseAmendmentLineRepository(ruled.AmendmentLines, policies.AmendmentLines, protection),
        store.Transactions);
}
```

Each `Protected*Repository` is a one-line subclass that also implements the application's repository interface
(`IPurchaseRepository`) and says how to restrict a criteria to one key. Subclass `ProtectedRepository` further to
protect what an application repository adds: `ProtectedProductRepository.FetchImage` reads the image as the product's
field it is, through `FetchById`.

In Blazor Server, one actor and one set of protected repositories per circuit:

```csharp
builder.Services.AddScoped<ActorSource>();
builder.Services.AddScoped(sp => ShoppingProtection.Protect(store.Repositories, sp.GetRequiredService<ActorSource>()));
```

On the REST server, one per request, with the actor read from the JWT; see [Remote repositories](remote.md).

## Testing the rules

`SecurityTestsBase` and `ErrataTestsBase` in `Shopping.Presentation.Tests` are written once and run twice: in process (`SecurityTests`, as Blazor Server runs them) and through the HTTP server (`SecurityHttpTests`). They log in as demo users and assert on the raw store, for example that a disjunctive criteria does not widen a customer's reach, or that a purchase made keeps its date for `Customer` and `Admin` alike.

## See also

- [Architecture](architecture.md)
- [Domain](domain.md)
- [Persistence](persistence.md)
- [Transactions](transactions.md)
- [Remote repositories](remote.md)
- [Update cycle](update-cycle.md), [Dispatcher](dispatcher.md), [Navigation](navigation.md), [History](history.md)
- [Getting started](../getting-started.md)
