# Domain model

The domain layer describes your entities once and lets every other layer work from that description: the SQL repositories, the JSON codec used by the REST layer, the HTTP client repositories and the presenters all read the same `EntityModel`. Queries and writes are expressed with two small tools, *projections* (a bean filled with markers that says which fields you want) and *criteria* (a typed filter class per entity), and go through one repository contract that is implemented both on the server, over a database, and on the client, over HTTP. Code that uses a repository does not know which side it runs on.

## Entities

- An entity is a plain class whose properties are all nullable (`long?`, `string?`, `User?`, `List<PurchaseItem>?`).
- `null` means "absent" in a result and "not wanted" in a projection. There is no separate "loaded" flag.
- Entities implement `IKeyedEntity`, exposing their key as `object? Key`.
- Each entity declares a static `EntityModel<T>`; nothing is discovered by reflection. The model holds names, getters and setters as delegates, so the domain stays compatible with trimming and Native AOT.

## Declaring an `EntityModel`

```csharp
public sealed class Purchase : IKeyedEntity
{
    public static readonly EntityModel<Purchase> Model = EntityModel.For(() => new Purchase())
        .Key(ValueKinds.Int64, "id", p => p.Id, (p, v) => p.Id = v)
        .Field(ValueKinds.Instant, "buyDate", p => p.BuyDate, (p, v) => p.BuyDate = v)
        .Reference("user", p => p.User, (p, v) => p.User = v, () => User.Model)
        .Collection("items", p => p.Items, (p, v) => p.Items = v, () => PurchaseItem.Model,
            () => new PurchaseItemCriteria())
        .Build();

    public long? Id { get; set; }
    public DateTimeOffset? BuyDate { get; set; }
    public User? User { get; set; }
    public List<PurchaseItem>? Items { get; set; }

    object? IKeyedEntity.Key => Id;
}
```

Rules:

- `EntityModel.For(factory, name)` starts the declaration; the name defaults to the class name.
- `Key(kind, name, get, set)` declares the key, a scalar. At most one key per entity.
- `Field(kind, name, get, set, inDefaultProjection: true)` declares a scalar. The `ValueKinds` catalogue covers `Int32`, `Int64`, `Double`, `Decimal`, `String`, `Boolean`, `Instant` (`DateTimeOffset`), `Date` (`DateOnly`), `Uuid`, `Bytes` and `Enum<TEnum>()`.
- `Reference(name, get, set, () => Other.Model)` declares an N:1 or 1:1 link. The model is passed as a delegate and resolved lazily, so two entities may refer to each other.
- `Collection(name, get, set, () => Child.Model, () => new ChildCriteria())` declares a 1:N list. The optional criteria factory lets a projection carry a filter for that collection (see below).
- Field names are the JSON names. Declaring a name twice, or two keys, throws at `Build()`.
- `inDefaultProjection: false` keeps a field out of the default projection: use it for blobs and secrets (`Product.Image`, `User.Password`). It still comes when a projection asks for it.

The same model is compiled into the server and the client. The SQL layer binds columns to these fields, and `JsonEntityCodec<T>` reads and writes JSON through them.

### What the model gives you

| Member | Result |
|---|---|
| `New()` | An empty instance. |
| `OwnFieldsProjection()` | The default projection: own scalars in the default projection, plus the key of each reference. Never collections. |
| `KeyProjection()` | A projection marking only the key. |
| `ChangedFields(next, previous)` | A projection marking the key and every scalar or reference that differs (references compare by key; collections are not compared). |
| `IsKeyOnly(projection)` | Whether the projection asks for nothing beyond the key. |
| `KeyOf(entity)` | The key value, boxed. |
| `Find(name)`, `Fields`, `Key` | The field models, for codecs and tooling. |

## Projections

A projection is an instance of the entity itself with markers in the fields you want. The marker values are never read, only their presence:

```csharp
var projection = new Purchase
{
    Id = Mark.Int64,
    BuyDate = Mark.Instant,
    User = new User { Id = Mark.Int64, Name = Mark.String },
    Items = Mark.List(
        new PurchaseItem { Id = Mark.Int64, Price = Mark.Decimal, Product = new Product { Name = Mark.String } },
        new PurchaseItemCriteria().Amount.Le(2).WithOrderBy(PurchaseItemOrderBy.MostExpensiveFirst),
        limit: 1),
};
var purchases = await purchaseRepository.Fetch(new PurchaseCriteria().WithProjection(projection));
```

Markers:

- Scalars: `Mark.Int32`, `Mark.Int64`, `Mark.Double`, `Mark.Decimal`, `Mark.String`, `Mark.Boolean`, `Mark.Instant`, `Mark.Date`, `Mark.Uuid`, `Mark.Bytes`, `Mark.Enum<TEnum>()`.
- References: a nested bean. An empty one (`new User()`) or one that marks only the key asks for the key only; marking more fields asks for the referenced row.
- Collections: `Mark.List(shape, criteria, limit, offset)` returns a `ProjectionList<T>`, a one-element list whose element is the shape of each item. The criteria (of the child's criteria type) filters and orders the children; `limit` and `offset` slice them per parent. Negative values throw.

What a projection means **for reads**:

- Only marked fields come back; everything else is `null`.
- The key always comes, at every level.
- Without a projection, a read uses `OwnFieldsProjection()`: no blobs, no secrets, no collections, references as key-only beans.
- A collection filter is the child entity's own criteria, translated by the child's repository. Ordering and slicing apply within each parent, not across all children.

What a projection means **for writes**:

- `Insert` writes the marked fields; `Update` writes the marked fields, and a marked field holding `null` clears the column.
- A reference marked only by its key writes just the foreign key. A reference marked beyond its key also writes the referenced row.
- A marked collection is written with its parent. On update the list is the *full state* of the collection: new children are inserted, known ones updated, missing ones deleted. See [persistence](persistence.md) for the details.
- `WriteProjection(projection)` adds the key at every nested level; the client uses it so the server can tell a child to update from a child to insert.

On the wire, a projection is a JSON object of the same shape. A collection with a filter or slice travels as an envelope `{ "shape": …, "where": …, "limit": …, "offset": … }`. This format is shared with the Kotlin implementation of Cube.

## Criteria

Each entity has a criteria class: one typed criterion per filterable field, declared in the constructor.

```csharp
public sealed class PurchaseCriteria : Criteria<PurchaseCriteria, Purchase, PurchaseOrderBy>
{
    public PurchaseCriteria()
    {
        PurchaseId = Comparable(ValueKinds.Int64, "purchaseId");
        UserId = Equality(ValueKinds.Int64, "userId");
        BuyDate = Comparable(ValueKinds.Instant, "buyDate");
        ProductId = Equality(ValueKinds.Int64, "productId");   // a field of the items, not of the purchase
        CustomerName = Text("customerName");                     // a field of the user
    }

    public ComparableCriterion<PurchaseCriteria, long?> PurchaseId { get; }
    public Criterion<PurchaseCriteria, long?> UserId { get; }
    public ComparableCriterion<PurchaseCriteria, DateTimeOffset?> BuyDate { get; }
    public Criterion<PurchaseCriteria, long?> ProductId { get; }
    public TextCriterion<PurchaseCriteria> CustomerName { get; }
}
```

A criterion name does not have to match a column, or even a field of the entity. `ProductId` and `CustomerName` above are *virtual*: the repository translates them through another table (see [persistence](persistence.md)).

### Criterion kinds

| Declared with | Type | Operations |
|---|---|---|
| `Equality(kind, name)` | `Criterion<TSelf, T>` | `Eq`, `Ne`, `In`, `IsNull`, `IsNotNull` |
| `Comparable(kind, name)` | `ComparableCriterion<TSelf, T>` | the above, plus `Gt`, `Ge`, `Lt`, `Le`, `Between` |
| `Text(name)` | `TextCriterion<TSelf>` | the above, plus `Like`, `ILike`, `Containing`, `StartingWith` |

Rules:

- Every operation returns the criteria object, so calls chain: `new ProductCriteria().Name.Containing("ball").Price.Le(50m)`.
- A `null` value asks nothing: `Eq(null)`, `Gt(null)` and `Like("")` leave the criterion untouched, so an empty screen field does not filter. Use `IsNull()` to ask for nulls.
- `In(null)` asks nothing; `In([])` matches nothing.
- `Between(a, b)` is inclusive; with one end `null` it becomes `Ge` or `Le`.
- `Containing` and `StartingWith` escape `%`, `_` and `\` in the text and compare ignoring case. `Like` and `ILike` use the wildcards you wrote.
- Requests on the same field combine with AND. Call `Or()` on the criterion first to combine them with OR: `criteria.Name.Or().Like("Volley%")` then `criteria.Name.Eq("Thread Seal Tape")`. `And()` restores the default.
- Different fields always combine with AND.
- `Clear()` removes the requests made on a field.

### Restrictions

`Restrict(value)` and `RestrictIn(values)` are set by the server to limit what the caller may reach ("only the purchases of user 7"):

- Restrictions never travel on the wire.
- They always combine with AND, outside the field's own requests. A client that sends `"or": true` widens only its own requests, never the restriction.
- `Clear()` does not remove them. `Restrict(null)` throws; `RestrictIn([])` matches nothing.
- A restriction that the repository cannot translate is an error, never a dropped filter.

### Ordering, projection and filter state

- The third type argument of `Criteria<TSelf, TEntity, TOrderBy>` is an enum of named orderings, named by their effect (`CheapestFirst`, `NameAToZ`), not by columns. The repository maps each name to columns and adds the key as a tie-breaker.
- On the wire an ordering travels in upper snake case (`CHEAPEST_FIRST`). An unknown ordering is refused with `InvalidRequestException`; an unknown operator is dropped, which can only make the filter looser.
- `WithOrderBy(value)` and `WithProjection(bean)` set the ordering and the projection fluently.
- `HasFilter` is true when any criterion has a request or a restriction.

The criteria wire format (shared with the Kotlin implementation):

```json
{ "price": { "p": [ { "o": "GE", "v": [10.5] }, { "o": "LE", "v": [50.5] } ] },
  "name": { "or": true, "p": [ { "o": "LIKE", "v": ["Volley%"] }, { "o": "EQ", "v": ["Thread Seal Tape"] } ] },
  "productId": 3,
  "orderBy": "CHEAPEST_FIRST" }
```

A loose value (`"productId": 3`) reads as equality.

## The repository contract

`IReadOnlyRepository<TEntity, TCriteria, TKey>` and `IRepository<TEntity, TCriteria, TKey>` are the only data access a use case or presenter sees. Declare a domain interface per entity and add operations there when needed:

```csharp
public interface IProductRepository : IRepository<Product, ProductCriteria, long>
{
    Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default);
}
```

| Method | Behavior |
|---|---|
| `Count(criteria)` | How many rows match. |
| `Fetch(criteria, offset, limit)` | The matching entities, shaped by `criteria.Projection` (or the default projection), in the criteria's ordering. `0` means "no skip" and "no limit". |
| `FetchPage(criteria, page, pageSize)` | One zero-based page with totals, as a `Page<T>`. |
| `FetchById(id, projection)` | The entity with that key, or `null`. |
| `OwnFieldsProjection()` | The repository's default projection. |
| `Insert(bean, projection)` | Inserts. Without a projection, writes the fields that hold a value. Unwritten columns get their database default. The bean comes back completed with the generated key and what the database filled. |
| `Update(newBean, oldBean, projection)` | Updates the row with `newBean`'s key (missing key: `InvalidRequestException`). Returns `false` when nothing was written. |
| `InsertOrUpdate(newBean, oldBean, projection)` | `Insert` when `oldBean` is `null`, otherwise `Update`. Never asks the database to decide. |
| `Delete(criteria)` | Deletes what matches and returns the count. A criteria without a filter is refused with `InvalidRequestException`. |

Which fields `Update` writes:

- With a projection: the marked fields. With `oldBean` too: only marked fields whose value changed.
- Without a projection, with `oldBean`: `ChangedFields(newBean, oldBean)`.
- With neither: `HeldFields(newBean)`, the fields that hold a value, as an insert. A null field is left alone, so a
  bean read with a narrow projection does not clear what it did not read; to clear a column, mark it in a projection.
- Collections and referenced rows are written only when a projection marks them.

Repositories never open or close transactions: they join the ambient one, and a write called with none runs in a transaction of its own. See [transactions](transactions.md).

`Page<T>` is a record `(Items, Number, TotalPages, TotalItems)`. `Page<T>.Of(items, page, pageSize, totalItems)` computes `TotalPages`, which is `0` when there are no items.

```csharp
var page = await products.FetchPage(
    new ProductCriteria().Name.Containing(search).WithOrderBy(ProductOrderBy.CheapestFirst),
    page: 0, pageSize: 20);
```

## See also

- [Architecture](architecture.md)
- [Update cycle](update-cycle.md)
- [Dispatcher](dispatcher.md)
- [Navigation](navigation.md)
- [History](history.md)
- [Persistence](persistence.md)
- [Transactions](transactions.md)
- [Protected repositories](protected-repositories.md)
- [Remote repositories and transactions](remote.md)
- [Getting started](../getting-started.md)
