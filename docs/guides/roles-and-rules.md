# Writing roles and rules

This guide shows how to write the `RepositoryPolicy` of an entity: who may do each operation, which rows each actor
reaches, which fields they read, what every write must satisfy, and how to let maintenance through without opening
the rules to everyone. It is a how-to; for how a protected repository runs a policy, and why the rules live in the
repository rather than in the presenters, read [Protected repositories](../concepts/protected-repositories.md) first.

The examples come from the Shopping sample (`samples/Shopping/Shopping.Domain/Security`) and from the framework's own
tests (`tests/Wdc.Persistence.Tests/ProtectedRepositoryTests.cs`). The framework types are in the `Wdc.Domain`
namespace (`src/Wdc.Domain/Security` and `src/Wdc.Domain/Cascade`).

## The shape of a policy

A policy is one object per entity, built from the entity's model and a criteria factory. Every rule is an optional
delegate; leave out what the entity does not need.

```csharp
// samples/Shopping/Shopping.Domain/Security/ProductPolicy.cs
public static RepositoryPolicy<Product, ProductCriteria, long> Create() => new(Product.Model, () => new ProductCriteria())
{
    Authorize = Permissions.Allow("product"),
};
```

The policy holds no actor. It is created once and every rule receives a `PolicyContext(Actor, InCascade)`, so the same
instance serves every session. Write each policy as a static factory (`ProductPolicy.Create()`) and pass it what its
rules need to read, such as the raw repositories (`PurchasePolicy.Create(store)`) or a clock
(`PurchaseAmendmentPolicy.Create(store, time)`).

## 1. Authorize each operation

`Authorize` is asked about a `RepositoryOperation` (`Insert`, `Update`, `Delete`, `Count`, `Fetch`, `FetchPage`,
`FetchById`) and throws `AccessDeniedException` to refuse. It runs first, before anything touches the database.

The framework does not interpret roles: `Actor` is an id and a set of strings, and `actor.HasRole(...)` is all it
offers. Keep the mapping from roles to operations in one place of the application. Shopping uses grants of the form
`entity:verb`:

```csharp
// samples/Shopping/Shopping.Domain/Security/Permissions.cs
public static void Require(Actor actor, string entity, RepositoryOperation operation)
{
    var verb = operation switch
    {
        RepositoryOperation.Insert or RepositoryOperation.Update => "write",
        RepositoryOperation.Delete => "delete",
        _ => "read",
    };
    if (!Has(actor, $"{entity}:{verb}"))
    {
        throw new AccessDeniedException($"Not allowed: {entity}:{verb}.");
    }
}

internal static Action<PolicyContext, RepositoryOperation> Allow(string entity) =>
    (context, operation) => Require(context.Actor, entity, operation);
```

A policy can also decide inline, which is fine for a rule that only one entity has:

```csharp
// tests/Wdc.Persistence.Tests/ProtectedRepositoryTests.cs
Authorize = (p, operation) =>
{
    if (operation == RepositoryOperation.Delete && !p.Actor.HasRole("keeper"))
    {
        throw new AccessDeniedException("Only a keeper deletes purchases.");
    }
},
```

Two things to keep in mind:

- `Authorize` also runs for an entity written in cascade (with `InCascade: true`). A customer who inserts a purchase
  with its items needs the grant to write items too.
- Nobody acting is not a role. With no actor, the protected repository throws `AuthenticationException` before
  `Authorize` is called.

## 2. Limit the reach

`Scope` narrows a criteria to the rows the actor may reach. It is applied to every read, and to update and delete.
`RestrictToId` turns a key into a criteria, so that the repository can check one row against the scope (fetch by id,
update, and children written in cascade).

```csharp
// samples/Shopping/Shopping.Domain/Security/PurchasePolicy.cs
Scope = (c, criteria) => Permissions.Reach(c, id => criteria.UserId.Restrict(id)),
RestrictToId = (criteria, id) => criteria.PurchaseId.Restrict(id),
```

`Permissions.Reach` restricts to the actor's own id unless the actor has `data:all`. Write your own helper the same
way when reach depends on the role.

Rules:

- Narrow with `Restrict`, never with `Eq`. A restriction is ANDed outside whatever the caller asked on the same field,
  so `UserId.Or()` plus `UserId.IsNotNull()` still returns only the actor's rows
  (`Reach_ADisjunctiveCriteriaDoesNotWidenIt` in the framework tests).
- A policy with a `Scope` or a `BeforeDelete` must set `RestrictToId`. Without it, the first operation that needs a
  single-key criteria throws `InvalidOperationException`.
- The scope changes the criteria the caller passed. Callers must not reuse it expecting the original.

What a row out of reach looks like to the caller (`Reach_GuardsFetchByIdUpdateAndDelete`):

| Operation | Row out of reach |
|---|---|
| `FetchById` | `null`, as if it did not exist |
| `Fetch`, `FetchPage`, `Count` | not counted, not returned |
| `Update` | `KeyNotFoundException` |
| `Delete` | not deleted (the count returned excludes it) |

A delete with no filter at all is refused with `InvalidRequestException` before the scope is added: "delete
everything I can reach" is not an operation the repository offers.

Reach is not a replacement for the presentation's own intent. The receipt asks for the logged-in user's purchase
explicitly, because a manager reaches every purchase but should still see only their own receipts there:

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Home/Receipt/ReceiptPresenter.cs
var criteria = new PurchaseCriteria()
    .PurchaseId.Eq(purchaseId)
    .UserId.Eq(subject.Id) // ownership: never only by key
```

## 3. Remove what the actor may not read

Two hooks keep fields away from the actor:

- `SanitizeProjection` edits the projection *before* the query, so the database is not even asked for the field.
- `SanitizeOutput` edits each result *after* it, and the bean returned by an insert.

Set both when a field must never leave. The user's password is the plain case:

```csharp
// samples/Shopping/Shopping.Domain/Security/UserPolicy.cs
SanitizeProjection = (_, p) => p.Password = null,
SanitizeOutput = (_, u) => u.Password = null,
```

A projection can reach the same entity through another one: a purchase projects its user, an errata projects its
author. The user policy does not run for those, because the user is not the entity being read. Each policy cleans
what its own projections can carry:

```csharp
// samples/Shopping/Shopping.Domain/Security/PurchasePolicy.cs
private static void Sanitize(PolicyContext context, Purchase purchase)
{
    UserPolicy.DropPassword(purchase.User);
    PurchaseAmendmentPolicy.Sanitize(context, purchase.Amendments);
}
```

A projected collection arrives as a `ProjectionList<T>`, whose `Shape` is the projection of one element.
`PurchaseAmendmentPolicy.Sanitize(PolicyContext, List<PurchaseAmendment>?)` handles both cases: it cleans the shape
when it is a projection and each element when it is data. Sanitizers can also depend on the role: the errata's
`Author` is removed for every actor whose reach is only their own data.

## 4. Check each write

The `Before*` hooks run inside the write's transaction. Throwing from one rolls back everything the write did,
children included. Throw `AccessDeniedException` for "you may not", `InvalidRequestException` for "this request is
wrong", and `KeyNotFoundException` for "no such row within reach"; over [REST](../concepts/remote.md) they become
403, 400 and 404.

### BeforeInsert: complete, or overwrite, what the server owns

Insert does not apply the scope; ownership on insert is `BeforeInsert`'s job. Treat what the client sent as a
request and set the fields that belong to the server:

```csharp
// samples/Shopping/Shopping.Domain/Security/PurchasePolicy.cs
BeforeInsert = (c, purchase, _) =>
{
    if (Permissions.ReachOf(c.Actor) is { } owner)
    {
        purchase.User = new User { Id = owner }; // bought by whoever is acting, whatever was sent
    }
    else if (purchase.User?.Id is null)
    {
        // Whoever reaches all the data may name the buyer; without one, it is themselves.
        purchase.User = new User { Id = long.Parse(c.Actor.Id, System.Globalization.CultureInfo.InvariantCulture) };
    }

    // The items, inserted in cascade, go through the purchase-item policy (price from the product).
    return ValueTask.CompletedTask;
},
```

The cart sends each line's price (`CartManager.Commit`), and `PurchaseItemPolicy.BeforeInsert` replaces it with the
product's current price, read through the raw store. The client's value never reaches the table.

### BeforeUpdate: look at what is being written

`BeforeUpdate(context, newBean, written, ct)` receives `written`, a projection that marks the fields of this update.
It comes from the caller's projection, or from the fields that changed against `oldBean`, or, failing both, from the
fields `newBean` holds. Test `written`, not `newBean`, to know whether a field is being changed:

```csharp
// samples/Shopping/Shopping.Domain/Security/UserPolicy.cs
BeforeUpdate = (_, u, written, _) =>
{
    if (written.Password is not null && u.Password is not null)
    {
        u.Password = PasswordHash.Hash(u.Password);
    }

    return ValueTask.CompletedTask;
},
```

Before `BeforeUpdate` runs, the repository has already checked that the row is within reach.

### BeforeDelete: the criteria of what would go

`BeforeDelete` receives the criteria of the delete, already narrowed by the scope, so it only sees rows the actor
could delete. When a rule needs to look at those rows, read them through the raw store and leave the criteria as you
found it:

```csharp
// samples/Shopping/Shopping.Domain/Security/PurchasePolicy.cs
internal static async Task<bool> AnyMade(ShoppingRepositories store, PurchaseCriteria criteria, CancellationToken cancellationToken)
{
    var projection = criteria.Projection;
    try
    {
        criteria.Projection = new Purchase { BuyDate = Mark.Instant };
        var purchases = await store.Purchases.Fetch(criteria, cancellationToken: cancellationToken);
        return purchases.Any(p => p.BuyDate is not null);
    }
    finally
    {
        criteria.Projection = projection;
    }
}
```

### Rules read the truth

A rule that needs data reads it through the *raw* repositories, not the protected ones. The rule must see what is in
the database, not the actor's view of it, and reading through a protected repository from inside a rule would apply
the actor's scope and sanitizers to the rule itself. This is why `PurchasePolicy.Create` takes the store.

## 5. What applies in cascade

A write reaches other entities through its projection: a purchase inserted with its items, a full-state update that
deletes orphans, a declared cascade delete. The raw repository writes those children, but reports each one to the
cascade guard, which runs the *child's* policy with `InCascade: true`:

| Child written in cascade | The child's policy runs |
|---|---|
| Inserted | `Authorize(Insert)` → `BeforeInsert` |
| Updated | `Authorize(Update)` → within reach → `BeforeUpdate` |
| Deleted | `Authorize(Delete)` → for each key: within reach → `BeforeDelete` with that key's criteria |

The sanitizers do not run for children written in cascade (they are about what the actor reads), and the child's
`Scope` applies only as the "within reach" check of an update or a delete.

Use `InCascade` when an entity may only be written through its parent, or only on its own:

```csharp
// samples/Shopping/Shopping.Domain/Security/PurchaseAmendmentLinePolicy.cs
BeforeInsert = (c, _, _) => c.InCascade || Permissions.Maintains(c.Actor)
    ? ValueTask.CompletedTask
    : throw new AccessDeniedException("A line enters only with its errata."),
```

`PurchaseItemPolicy` does the opposite check: an item inserted on its own into a purchase already made is refused,
while one inserted with its purchase (`InCascade`) enters.

For cascades to be guarded, every entity a write can reach must have a protected repository in the *same*
`Protection`. A child whose entity is not registered there is refused with `AccessDeniedException` ("not protected
here"). Build all of a session's protected repositories together, as `ShoppingProtection.Protect` does.

What a rule writes through the raw store is not reported: rules run outside the guard, both the parent's and each
child's. That is what lets a rule apply a correction the actor could not make directly (section 7).

## 6. Leave room for maintenance

Business rules hold for every role of the application, administrators included. Some systems still need a role that
can repair data, for example to remove an item from a purchase recorded by mistake. Make that role explicit, and
relax specific checks for it instead of skipping the policy:

```csharp
// samples/Shopping/Shopping.Domain/Security/Permissions.cs
public static bool Maintains(Actor actor)
{
    ArgumentNullException.ThrowIfNull(actor);
    return actor.HasRole(nameof(Role.Super));
}
```

Each rule then decides whether it is relaxable:

```csharp
// samples/Shopping/Shopping.Domain/Security/PurchasePolicy.cs
BeforeDelete = async (c, criteria, ct) =>
{
    if (!Permissions.Maintains(c.Actor) && await AnyMade(store, criteria, ct))
    {
        throw new AccessDeniedException("The purchase was already made: it is not deleted.");
    }
},
```

In Shopping, `Super` may change, delete or add items to a purchase already made, and may update or delete an errata.
`Admin` has every grant and still may not: "a purchase made does not change" is a business rule, not a permission.
What stays in force for `Super`:

- `Authorize`: `Super` has every grant, but the hook still runs.
- Data that must never leave: no role gets a password back.
- Values the server owns: an item inserted by `Super` still takes the product's price
  (`Super_AmendsItsItems_AndANewOneTakesTheProductsPrice`), and `Super` cannot write an item's price on update
  either (`Super_DoesNotWriteThePriceOfAnItem`): `BeforeUpdate` checks the price before it returns early.

When you relax a rule, return early only past the checks that are meant to be relaxed, and keep the others before
the early return.

## 7. Model a correction as its own operation

When a record must not change, but mistakes in it must be fixable by people who are not maintenance, do not open the
update. Add an entity whose insertion *is* the correction, and give its policy the job of applying it. The record of
the correction (who, when, why, what) comes for free, because it is the entity.

In Shopping a purchase already made never changes for an application role. `Admin` and `Manager` correct one by
inserting a `PurchaseAmendment` (an errata) with one `PurchaseAmendmentLine` per item corrected. The presenter only
says what the user asked:

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Home/Admin/Amend/AmendPresenter.cs
var errata = new PurchaseAmendment
{
    Purchase = new Purchase { Id = purchaseId },
    Reason = Scope.Reason.Trim(),
    Lines = [.. Scope.Lines.Where(l => l.NewQuantity < l.Quantity).Select(l => new PurchaseAmendmentLine
    {
        ItemId = l.ItemId,
        AmountBefore = l.Quantity, // what was on screen: refused if someone corrected it meanwhile
        AmountAfter = l.NewQuantity,
    })],
};
```

`PurchaseAmendmentPolicy.BeforeInsert` does the rest (`Apply` in the same file):

1. **Refuse what is not a correction.** Not in cascade, with a purchase, a reason and at least one line.
2. **Lock the purchase.** Read it within the actor's reach (else `KeyNotFoundException`), require it to be made, and
   write `BuyDate` back to the value it holds through the raw store. Writing the row takes its lock in every
   database, so two errata of the same purchase run one after the other, each on what the other left.
3. **Validate against the current data.** Each line names an item of that purchase, once; `AmountBefore`, if sent,
   must match the item (otherwise someone corrected it meanwhile); the amount only goes down (0 removes the item);
   not every item is removed.
4. **Apply it through the raw store**, in the insert's transaction: `store.PurchaseItems.Update` or `Delete`. The
   generic item update stays refused to every application role.
5. **Fill the record from the data**, whatever was sent: each line's previous amount, price and product, the date
   from the `TimeProvider`, the author from the actor.

The lines are then inserted in cascade and pass `PurchaseAmendmentLinePolicy`, which admits a line only in cascade.
Once inserted, the errata and its lines do not change and are not deleted, except by `Super`.

The pattern, in general:

- The correction is narrower than an update: it says what may change (here, amounts going down), and every other
  field stays out of reach.
- The rule reads and writes through the raw store, inside the protected write's transaction, so the correction and
  its record commit or roll back together.
- The server fills in every field that describes the past (previous values, date, author). The client's version of
  them is ignored, or checked and refused when it disagrees.

## 8. Protect what the repository adds

When an application repository has members beyond `IRepository`, subclass `ProtectedRepository` and call `Enter`
with the operation they amount to. `Enter` checks that someone is acting and runs `Authorize`:

```csharp
// samples/Shopping/Shopping.Domain/Security/ProtectedProductRepository.cs
public sealed class ProtectedProductRepository(IProductRepository inner, Protection protection)
    : ProtectedRepository<Product, ProductCriteria, long>(inner, ProductPolicy.Create(), protection), IProductRepository
{
    public Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        Enter(RepositoryOperation.FetchById);
        return inner.FetchImage(productId, cancellationToken);
    }
}
```

## 9. Test the rules

Test the policy through the repositories a session gets, and assert on the raw store:

- `Shopping.Presentation.Tests/SecurityTestsBase.cs` and `ErrataTestsBase.cs` log in as demo users and try what each
  role may and may not do. Each base runs twice: in process (`SecurityTests`, `ErrataTests`) and through the HTTP
  server (`SecurityHttpTests`, `ErrataHttpTests`), so the rules are shown to hold over the wire too.
- `tests/Wdc.Persistence.Tests/ProtectedRepositoryTests.cs` exercises the framework with small inline policies: reach,
  sanitizing, the `written` marks, rollback on refusal, and every cascade case.

Cover, for each rule, the role it allows, the role it refuses, and the case where the request tries to go around it
(a disjunctive criteria, a field sent that the server owns, a child written through its parent).

## Checklist

- [ ] `Authorize` covers every operation, including the ones the entity is written through in cascade.
- [ ] `Scope` uses `Restrict`; `RestrictToId` is set whenever `Scope` or `BeforeDelete` is.
- [ ] Every field that must not leave is removed by both sanitizers, and by the policies of the entities that project it.
- [ ] `BeforeInsert` sets what the server owns (owner, price, dates, author).
- [ ] `BeforeUpdate` tests `written`; rules read through the raw store.
- [ ] Every entity reachable by cascade is protected in the same `Protection`.
- [ ] Relaxations for maintenance are explicit and leave the other checks in force.

## See also

- [Protected repositories](../concepts/protected-repositories.md): the order in which a protected repository runs a policy.
- [Persistence](../concepts/persistence.md): writes in cascade and the cascade guard.
- [Transactions](../concepts/transactions.md): the transaction the hooks run in.
- [Remote](../concepts/remote.md): the same rules behind REST routes.
- [The Shopping sample](../samples/shopping.md)
