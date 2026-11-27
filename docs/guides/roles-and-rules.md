# Writing roles and rules

This guide shows how to protect an entity's data:
- write its **data policy**: which rows each role reaches, which fields it reads, writes or sees masked, and whether
  it deletes;
- keep the **business rules** apart: what holds whoever acts, and how maintenance gets through without opening the
  rules to everyone.

It is a how-to. For how a protected repository applies the rules, and why they live in the repository rather than in
the presenters, read [Protected repositories](../concepts/protected-repositories.md) first.

The examples come from the Shopping sample (`samples/Shopping/Shopping.Domain/Security`) and from the framework's
tests (`tests/Wdc.Domain.Tests/Policy`, `tests/Wdc.Persistence.Tests/DataPolicyRepositoryTests.cs`). The framework
types are in the `Wdc.Domain` namespace.

## 1. Start the policy

A data policy is one object per entity, built from the entity's model and a criteria factory, and finished with
`Build()`:

```csharp
// samples/Shopping/Shopping.Domain/Security/ProductPolicy.cs
private static readonly string[] s_fields = ["name", "price", "description", "image"];

public static DataPolicy<Product, ProductCriteria> Build() =>
    DataPolicy.For(Product.Model, () => new ProductCriteria())
        .Criterion("productId", "id")
        .Orderings(c => c.OrderBy switch
        {
            ProductOrderBy.NameAToZ => ["name"],
            ProductOrderBy.CheapestFirst or ProductOrderBy.MostExpensiveFirst => ["price"],
            _ => [],
        })
        .Grant(["Super", "Admin", "Manager"], g => g.Read(s_fields).Create(s_fields).Update(s_fields).Delete())
        .Grant(["Customer"], g => g.Read(s_fields))
        .Build();
```

The policy holds no actor: `AccessFor(actor)` applies it to one, so the same instance serves every session. Fields are
named as in the entity model. `Build()` checks every name, and fails on a gap or a contradiction, so a mistake shows
when the policy is built.

## 2. Say what each criterion and ordering reveals

Filtering or sorting by a field reveals its values as much as reading it, so the policy must know which field each
criterion and each ordering touches:

- A criterion named as its field (`name`) needs nothing.
- `Criterion("userId", "user")` maps a criterion to a field of another name.
- `Criterion("customerName", "user.name")` maps one to a field of another entity, through a reference. Filtering by it
  takes reading `user` here **and** `name` in the user's policy.
- `Criterion("search", "name", "description")` maps a term searched in several fields (translated with
  `FilterContext.TranslateAny`). Filtering by it takes reading them all.
- `Orderings(criteria => fields)` names the fields of each ordering. The key, the tie-breaker, never counts.

A criterion that maps to no field fails `Build()`.

## 3. Give each role its grants

`Grant(roles, g => ...)` adds a grant to each role named. A grant says:

| Call | What it allows |
|---|---|
| `Read(fields)` | Reading the fields as they are, and filtering and sorting by them. The key is always read. |
| `Mask(Mask.KeepFirst(n) or KeepLast(n), fields)` | Reading text fields partially (`"j***"`): not filtering or sorting by them, not writing them in the same grant. |
| `Create(fields)` | Setting the fields when creating a row. A reference is sent by its key. |
| `Update(fields)` | Changing the fields of a reachable row. |
| `Delete()` | Deleting the reachable rows. |

- An actor with several roles gets every grant of every role; a request passes when **one** grant allows all of it.
  So "filter by name and by login" needs one grant that reads both.
- A role that reaches different rows with different fields has one grant per set of rows: one's own user in full,
  everyone's name.
- A grant named for several roles counts once.

## 4. Limit the rows

`Rows(scope)` limits a grant to the rows an `IRowScope` reaches. The scope says it twice, and both must agree:

```csharp
// samples/Shopping/Shopping.Domain/Security/PurchaseItemPolicy.cs
internal sealed class OwnItems(ShoppingRepositories store) : IRowScope<PurchaseItem, PurchaseItemCriteria>
{
    public void Restrict(PurchaseItemCriteria group, Actor actor) => group.UserId.Eq(ShoppingPolicies.IdOf(actor));

    public async ValueTask<bool> Covers(PurchaseItem row, Actor actor, CancellationToken cancellationToken) =>
        await Owners.OfPurchase(store, row.Purchase, cancellationToken) == ShoppingPolicies.IdOf(actor);

    public PurchaseItem? Projection() => new() { Purchase = new Purchase { User = new User { Id = Mark.Int64 } } };
}
```

- `Restrict` fills an empty criteria with what selects the rows. The policy adds one such group per scope to the
  request, as an alternative, so a request cannot widen it.
- `Covers` checks one row in memory: a row read, to decide its fields; a row created; the stored row of an update, and
  the row as it would be after it.
- `Projection` names what `Covers` reads beyond the key. It is fetched for the check and cleared before the rows leave.
- `Covers` may look up what the row does not carry, through the trusted inside (the store). An item sent with only its
  purchase's key looks the purchase's owner up.
- Policies compare scopes by identity. Declare a scope once (a `static readonly` instance, or one per policy built)
  and share it.

## 5. Fields that never leave

- `WriteOnly("password")`: no grant may read or mask it, and asking for it is refused, even without an actor. It is
  out of the default shape.
- Every refusal is an `AccessDeniedException` naming the fields: the client asked for something its roles do not
  allow, and a correct client never does. Only a read **without** a projection is trimmed to what the actor may read.

## 6. References and collections

A reference or a collection is one field of the grant (`Read("user")`). What is read **inside** it goes through the
policy of the entity it points to, row by row. So an entity has one set of rules wherever it appears:

```csharp
// samples/Shopping/Shopping.Domain/Security/PurchasePolicy.cs
DataPolicy.For(Purchase.Model, () => new PurchaseCriteria())
    .Reference("user", () => all.Users)
    .Collection("items", inverse: "purchase", () => all.Items)
    .Collection("amendments", inverse: "purchase", () => all.Amendments)
```

- Every reference and collection of the model needs its policy, or `Build()` fails.
- The policies refer to each other, so they are given as functions and built together. In Shopping,
  `ShoppingPolicies` builds the six and hands each one the others.
- `inverse` names the reference of each child that points back to its owner. A child of a new row is checked as a
  row of it.

## 7. Writes and their children

The checks of a write, and of every child it writes in cascade:

- **Insert.** One grant must allow every field sent and reach the new row. Each child of a collection must be creatable
  and reached by its own policy, as a row of the new one: a customer's new purchase, and each of its items, must be
  theirs.
- **Update.** One grant must reach the stored row, allow every changed field, and still reach the row after the change.
  When the update carries a collection (its full state), each child answers to its own policy:
  - a new child needs a create grant;
  - a changed one needs an update grant for the fields that changed;
  - an orphan needs a delete grant.

  A child sent as it is stored needs none.
- **Delete.** Only the rows of grants that delete, which must read what the criteria filters by. The children deleted
  with their parent need a delete grant of their own.
- An update of a row the actor cannot reach is "not found" when they cannot read it either, and "not allowed" when they
  can.

Everything a write does runs in one transaction: a refusal anywhere rolls back the parent and every child.

## 8. Keep the business rules apart

What holds whoever acts is not access, and has no place in a policy:
- a price taken from the product;
- a record that does not change once made;
- a password stored as its hash.

Put it in repositories that wrap the store and implement the same contract, between the protected repositories and
the store:

```csharp
// samples/Shopping/Shopping.Domain/Security/Rules/PurchaseItemRules.cs
public override async Task<bool> Insert(PurchaseItem bean, PurchaseItem? projection = null, CancellationToken cancellationToken = default)
{
    if (bean.Purchase?.Id is not { } purchaseId)
    {
        throw new InvalidRequestException("An item needs its purchase.");
    }

    if (!Maintains && await PurchaseRules.AnyMade(store, new PurchaseCriteria().PurchaseId.Eq(purchaseId), cancellationToken))
    {
        throw new AccessDeniedException("The purchase was already made: no item enters it.");
    }

    bean.Price = await Prices.Of(store, bean.Product?.Id, cancellationToken);
    return await base.Insert(bean, projection, cancellationToken);
}
```

- **They run after the policy**, inside the same transaction, and read the truth through the store.
- **Children written in cascade** do not go through the rule repositories: the store writes them. A rule about
  children belongs to the parent's rules. For example, `PurchaseRules` takes each item's price from its product, on
  insert and on update.
- **Server-owned fields:** the rules set them, whatever was sent (a price, a date, an author).

## 9. Leave room for maintenance

A maintenance role needs to fix what the application's roles may not touch.
- **In the policy:** it gets the grants (`Update`, `Delete`) the others lack.
- **In the business rules:** the rules that may be relaxed check who acts. `ShoppingRules` is built per session with
  the `IActorSource`, and `Permissions.Maintains(actor)` is true for `Super`.

Relax only what maintenance needs: in Shopping, a purchase made changes for `Super`, but an item's price still comes
from its product.

## 10. Model a correction as its own operation

When a record must not change, but mistakes in it must be fixable by people who are not maintenance, do not open the
update. Add an entity whose insertion **is** the correction, and let its rules apply it. The record of the correction
(who, when, why, what) comes for free, because it is the entity.

In Shopping, `Admin` and `Manager` correct a purchase by inserting a `PurchaseAmendment` (an errata) with one
`PurchaseAmendmentLine` per item corrected. Their grants allow creating errata, not updating items.
`PurchaseAmendmentRules.Insert` then works in five steps:

1. **Refuse what is not a correction:** a purchase, a reason and at least one line are required.
2. **Lock the purchase:** require it to be made, and write `BuyDate` back to the value it holds. Writing the row takes
   its lock in every database, so two errata of the same purchase run one after the other.
3. **Validate against the current data.**
   - Each line names an item of that purchase, once.
   - `AmountBefore` must match the item; otherwise someone corrected it meanwhile.
   - The amount only goes down, and 0 removes the item. Not every item may be removed.
4. **Apply it through the store**, in the insert's transaction.
5. **Fill the record from the data**, whatever was sent: each line's previous amount, price and product, the date from
   the `TimeProvider`, and the author from the actor.

The lines are inserted in cascade with the errata, through the line policy. The line rules refuse a line inserted by
itself.

## 11. Protect what the repository adds

When an application repository has members beyond `IRepository`, subclass `ProtectedRepository` and go through the
policy. `FetchImage` reads the image as the product's field it is:

```csharp
// samples/Shopping/Shopping.Domain/Security/ProtectedProductRepository.cs
public async Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
{
    var product = await FetchById(productId, new Product { Image = Mark.Bytes }, cancellationToken)
                  ?? throw new KeyNotFoundException($"No product {productId}.");
    return product.Image;
}
```

## 12. When a declaration is not enough: hooks

An entity whose access no declaration expresses can be protected by hooks instead: a `RepositoryPolicy` of optional
delegates, given to the other constructor of `ProtectedRepository`. The hooks are `Authorize`, `Scope`,
`RestrictToId`, `SanitizeProjection`, `SanitizeOutput`, `BeforeInsert`, `BeforeUpdate` and `BeforeDelete`.

- It is one or the other: an entity has a data policy or hooks, never both.
- A hook may still apply a data policy: `policy.AccessFor(context.Actor).PrepareRead(criteria)`, or `CheckCreate`,
  `CheckUpdate` and `RestrictDelete`.
- Scope with `Restrict`, never `Eq`, so a disjunctive request cannot widen the reach.
- A child written in cascade runs its own hooks with `InCascade: true`.

`tests/Wdc.Persistence.Tests/ProtectedRepositoryTests.cs` shows each hook at work.

## 13. Test the rules

Test through the repositories a session gets, and assert on the store:

- **The policy alone.** `tests/Wdc.Domain.Tests/Policy/DataPolicyTests.cs` checks it with no database: refusals by
  name, rows as groups, fields per row, masks, writes, declaration errors.
- **Protected repositories on a database.** `tests/Wdc.Persistence.Tests/DataPolicyRepositoryTests.cs` covers reads,
  reach, and the children of an insert, an update and a delete.
- **The application.** `Shopping.Presentation.Tests/SecurityTestsBase.cs` and `ErrataTestsBase.cs` log in as demo
  users and try what each role may and may not do. Each runs in process and through the HTTP server, so the rules are
  shown to hold over the wire too.

Cover, for each rule:
- the role it allows;
- the role it refuses;
- the request that tries to go around it: a disjunctive criteria, a field the server owns, a child written through its
  parent, a filter on a field of another entity.

## Checklist

- [ ] Every criterion and ordering says which fields it reveals.
- [ ] Each role reaches only its rows (`Rows`), and `Restrict` and `Covers` agree.
- [ ] Every field that must not be read is `WriteOnly`, or out of every grant.
- [ ] Every reference and collection names its policy, and the children's grants allow what their parent writes.
- [ ] Server-owned fields are set by the business rules, whatever was sent.
- [ ] Relaxations for maintenance are explicit and leave the other rules in force.

## See also

- [Protected repositories](../concepts/protected-repositories.md): how the rules are applied, in each operation and in
  cascade.
- [Persistence](../concepts/persistence.md): writes in cascade and the cascade guard.
- [Transactions](../concepts/transactions.md): the transaction the rules run in.
- [Remote](../concepts/remote.md): the same rules behind REST routes.
- [The Shopping sample](../samples/shopping.md)
