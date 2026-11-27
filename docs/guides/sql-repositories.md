# Writing a SQL repository

This guide adds a new entity to an application and stores it in PostgreSQL or SQL Server: the entity and its
`EntityModel`, its criteria and named orderings, the table, the `EntityMapping` that ties them together, the
translation of the criteria, references and collections written in cascade, and finally the `Database` that the
repositories run on and the transactions that group their writes. It is a how-to; for how a `SqlRepository` builds its
statements and why, read [Persistence](../concepts/persistence.md) and [Domain](../concepts/domain.md).

The examples come from the Shopping sample: the domain in `samples/Shopping/Shopping.Domain`, the repositories and the
schema in `samples/Shopping/Shopping.Persistence`. The framework's own contract tests are in
`tests/Wdc.Persistence.Tests/RepositoryContractTests.cs`.

## Projects and packages

| Layer | References | Contains |
|---|---|---|
| Domain (shared by server and clients) | `Wdc.Domain` | Entities, models, criteria, orderings, repository interfaces. |
| Persistence (server only) | `Wdc.Persistence.PostgreSql` and/or `Wdc.Persistence.SqlServer` | Mappings, `SqlRepository` subclasses, DDL. |

Each dialect package brings `Wdc.Persistence` and its driver (Npgsql, or Microsoft.Data.SqlClient). Keep the domain
free of them: the same domain assembly is compiled into a WebAssembly or desktop client that talks HTTP.

## 1. Declare the entity and its model

An entity is a class with nullable properties, a static `EntityModel<T>` and `IKeyedEntity`:

```csharp
// samples/Shopping/Shopping.Domain/Products/Product.cs
public sealed class Product : IKeyedEntity
{
    public static readonly EntityModel<Product> Model = EntityModel.For(() => new Product())
        .Key(ValueKinds.Int64, "id", p => p.Id, (p, v) => p.Id = v)
        .Field(ValueKinds.String, "name", p => p.Name, (p, v) => p.Name = v)
        .Field(ValueKinds.Decimal, "price", p => p.Price, (p, v) => p.Price = v)
        .Field(ValueKinds.String, "description", p => p.Description, (p, v) => p.Description = v)
        .Field(ValueKinds.Bytes, "image", p => p.Image, (p, v) => p.Image = v, inDefaultProjection: false)
        .Build();

    public long? Id { get; set; }
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    public string? Description { get; set; }
    public byte[]? Image { get; set; }

    object? IKeyedEntity.Key => Id;
}
```

- The field names (`"id"`, `"name"`) are the JSON names and the names the mapping refers to.
- Mark blobs and secrets with `inDefaultProjection: false`, so a read that does not ask for them does not carry them.
- A reference is `.Reference("user", p => p.User, (p, v) => p.User = v, () => User.Model)`; a collection is
  `.Collection("items", p => p.Items, (p, v) => p.Items = v, () => PurchaseItem.Model, () => new PurchaseItemCriteria())`
  (see `Purchase.cs`). The models are resolved lazily, so entities may point at each other.

## 2. Declare the criteria and the orderings

One criterion per filterable field, declared in the constructor, plus an enum of named orderings:

```csharp
// samples/Shopping/Shopping.Domain/Products/ProductCriteria.cs
public sealed class ProductCriteria : Criteria<ProductCriteria, Product, ProductOrderBy>
{
    public ProductCriteria()
    {
        ProductId = Comparable(ValueKinds.Int64, "productId");
        Name = Text("name");
        Price = Comparable(ValueKinds.Decimal, "price");
        Description = Text("description");
    }

    public ComparableCriterion<ProductCriteria, long?> ProductId { get; }
    public TextCriterion<ProductCriteria> Name { get; }
    public ComparableCriterion<ProductCriteria, decimal?> Price { get; }
    public TextCriterion<ProductCriteria> Description { get; }
}
```

```csharp
// samples/Shopping/Shopping.Domain/Products/ProductOrderBy.cs
public enum ProductOrderBy { OldestFirst, NewestFirst, NameAToZ, CheapestFirst, MostExpensiveFirst }
```

- Use `Equality` for keys and foreign keys you only compare, `Comparable` for numbers and dates, `Text` for strings.
- A criterion may be *virtual*: `PurchaseItemCriteria.UserId` filters items by the owner of their purchase, a column
  of another table. Step 5 shows how it is translated.
- Name orderings by their effect. A criteria that names none is ordered by the key, ascending.

Then the repository interface, in the domain:

```csharp
// samples/Shopping/Shopping.Domain/Products/IProductRepository.cs
public interface IProductRepository : IRepository<Product, ProductCriteria, long>
{
    Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default);
}
```

An entity with nothing to add still gets its own interface (`IPurchaseItemRepository`), so use cases depend on it and
not on `IRepository<…>` directly.

## 3. Create the table

The framework does not generate DDL. Write it per dialect and run it with `Database.Execute`. The Shopping sample keeps
it in `ShoppingStore.cs` (`ShoppingSchema`):

```csharp
// samples/Shopping/Shopping.Persistence/ShoppingStore.cs (PostgreSQL)
yield return $"""
    CREATE TABLE "{s}".product (
        id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        name text NOT NULL,
        price numeric(20, 2) NOT NULL,
        description text NOT NULL,
        image bytea)
    """;
yield return $"CREATE INDEX ix_product_name ON \"{s}\".product (name)";
```

```csharp
// samples/Shopping/Shopping.Persistence/ShoppingStore.cs (SQL Server)
yield return $"""
    CREATE TABLE [{s}].[product] (
        id bigint IDENTITY(1, 1) PRIMARY KEY,
        name nvarchar(1000) NOT NULL,
        price decimal(20, 2) NOT NULL,
        description nvarchar(max) NOT NULL,
        image varbinary(max) NULL)
    """;
```

- The generated SQL always quotes table and column names. In PostgreSQL a quoted name is case-sensitive: create tables
  and columns in lower case, as above, and map them with the same spelling.
- `ValueKinds.Instant` goes in `timestamptz` (PostgreSQL) or `datetimeoffset` (SQL Server); `Bytes` in `bytea` or
  `varbinary(max)`; `Decimal` in `numeric` or `decimal`. An enum field (`ValueKinds.Enum<TEnum>()`) is written by its
  name: give it a text column.
- Index the foreign-key column of every collection (`purchase_item (purchase_id)`): each collection read is a subquery
  on it. Index the columns of the orderings you use on large tables.
- A key the database assigns (`IDENTITY`) is mapped with `generated: true` in the next step.

To create the schema on first start, check `information_schema` and run the statements in order, as
`ShoppingStore.EnsureCreated` does:

```csharp
// samples/Shopping/Shopping.Persistence/ShoppingStore.cs
var exists = await Database.Scalar(Statement(
    "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @p0 AND table_name = 'product'", schema),
    cancellationToken);
if (Convert.ToInt64(exists, System.Globalization.CultureInfo.InvariantCulture) > 0)
{
    return false;
}

foreach (var ddl in ShoppingSchema.Create(Provider, schema))
{
    await Database.Execute(Statement(ddl), cancellationToken);
}
```

`Statement(sql, params)` there is `new SqlStatement(sql, parameters)`; parameters are referenced as `@p0`, `@p1`…
For a real deployment, run the same DDL from your migration tool instead.

## 4. Map the entity and write the repository

Subclass `SqlRepository<TEntity, TCriteria, TKey>`, implement the domain interface, and keep the mapping in a public
static field so other mappings can point at it:

```csharp
// samples/Shopping/Shopping.Persistence/ProductRepository.cs
public sealed class ProductRepository(Database database)
    : SqlRepository<Product, ProductCriteria, long>(database, Mapping), IProductRepository
{
    public static readonly EntityMapping<Product> Mapping = EntityMapping.For(Product.Model, "product", "pr")
        .Key("id", generated: true)
        .Column("name")
        .Column("price")
        .Column("description")
        .Column("image")
        .Conditions<ProductCriteria>(Conditions)
        .Ordering<ProductOrderBy>(o => o switch
        {
            ProductOrderBy.NewestFirst => [SortSpec.Desc("id")],
            ProductOrderBy.NameAToZ => [SortSpec.Asc("name")],
            ProductOrderBy.CheapestFirst => [SortSpec.Asc("price")],
            ProductOrderBy.MostExpensiveFirst => [SortSpec.Desc("price")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    public async Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        var product = await FetchById(productId, new Product { Image = Mark.Bytes }, cancellationToken)
                      ?? throw new KeyNotFoundException($"No product {productId}.");
        return product.Image;
    }

    private static ISqlFragment? Conditions(ProductCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.ProductId),
        t.Translate("name", criteria.Name),
        t.Translate("price", criteria.Price),
        t.Translate("description", criteria.Description),
    ]);
}
```

- `EntityMapping.For(model, table, aliasPrefix)`: the prefix names the aliases of generated SQL; pick a short one per
  entity.
- `Column(field, column)` defaults the column to the field name. Use the second argument when they differ
  (`.Column("buyDate", "buy_date")`).
- `SortSpec.Asc`/`Desc` name columns of this table. The key is added as the last tie-breaker, so pages are stable.
- Map every field you want stored. A field left out is never read nor written. A field that does not exist, has
  another kind, or is mapped twice throws when the mapping is built, at type initialization.
- Extra operations are compositions of the inherited ones (`FetchImage` above). Validation goes in an override:
  `PurchaseItemRepository.Insert` refuses an amount that is not positive, and it also runs when an item is written in
  cascade with its purchase.

## 5. Translate the criteria

`Conditions` receives the criteria and a `FilterContext`, and returns one SQL fragment (or `null` for no filter):

- `t.Translate(column, criterion)` covers every operation of the criterion (`Eq`, `In`, `Between`, `Containing`…)
  and the server's restrictions. It returns `null` when the criterion asks nothing.
- `Sql.And([...])` and `Sql.Or([...])` skip the `null` entries.
- Translate every criterion the class declares. A criterion that arrives filled and is never translated makes the
  statement throw `InvalidOperationException`, so a forgotten filter is a failure, not a wider result.

Two more translations:
- **The alternative groups** of the criteria (`criteria.AnyOf`). Each repository translates them with its own
  `Conditions`, as one more entry of the `And`. Without that line, the groups are ignored and the filter gets looser:

  ```csharp
  t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
  ```

- **A term over several columns**, with `t.TranslateAny(["name", "description"], criteria.Search)`. It applies the
  whole criterion to each column and joins the columns with OR. A criterion with a restriction is refused there,
  since in an OR it would stop restricting.

A virtual criterion becomes an `EXISTS` subquery correlated by a foreign key:

```csharp
// samples/Shopping/Shopping.Persistence/PurchaseItemRepository.cs
private static ISqlFragment? ExistsPurchaseOfUser(PurchaseItemCriteria criteria, FilterContext t)
{
    if (!FilterContext.Asks(criteria.UserId))
    {
        return null;
    }

    var purchase = t.NewAlias("pu");
    var onUser = t.Translate(Sql.Column(purchase, "user_id"), criteria.UserId);
    return Sql.Exists(w =>
    {
        w.Append("SELECT 1 FROM ").Table("purchase").Append(' ').Identifier(purchase).Append(" WHERE ");
        Sql.And([Sql.Equal(Sql.Column(purchase, "id"), t.Column("purchase_id")), onUser])!.Render(w);
    });
}
```

- Check `FilterContext.Asks` first, and skip the subquery when the criterion asks nothing.
- `t.NewAlias(prefix)` gives an alias unused in the statement; `t.Column(name)` is a column of the filtered table;
  `w.Table(name)` adds the schema and quotes.
- Pass the criterion to `t.Translate(expression, criterion)` even inside the subquery: that is what marks it as
  translated, and what applies its restrictions.

The same `Conditions` filters this repository's queries and the collections of other entities that reach it (a
purchase's `items` projected with a `PurchaseItemCriteria`).

## 6. Map references and collections

A reference is a foreign key in this table; a collection is a foreign key in the child's table. Both point at the
other repository's mapping through a delegate, so the order of declaration and cycles do not matter:

```csharp
// samples/Shopping/Shopping.Persistence/PurchaseRepository.cs
public static readonly EntityMapping<Purchase> Mapping = EntityMapping.For(Purchase.Model, "purchase", "pu")
    .Key("id", generated: true)
    .Column("buyDate", "buy_date")
    .Reference("user", "user_id", () => UserRepository.Mapping)
    .Collection("items", "purchase_id", () => PurchaseItemRepository.Mapping, cascadeDelete: true)
    .Collection("amendments", "purchase_id", () => PurchaseAmendmentRepository.Mapping, cascadeDelete: true)
    .Conditions<PurchaseCriteria>(Conditions)
    // .Ordering<PurchaseOrderBy>(...)
    .Build();
```

```csharp
// samples/Shopping/Shopping.Persistence/PurchaseItemRepository.cs
public static readonly EntityMapping<PurchaseItem> Mapping = EntityMapping.For(PurchaseItem.Model, "purchase_item", "pi")
    .Key("id", generated: true)
    .Column("amount")
    .Column("price")
    .Reference("purchase", "purchase_id", () => PurchaseRepository.Mapping)
    .Reference("product", "product_id", () => ProductRepository.Mapping)
    // .Conditions / .Ordering
    .Build();
```

To have a collection written with its parent:

1. The child mapping maps a reference back to the parent **on the same column** (`PurchaseItem.purchase` on
   `purchase_id`). Without it, the first cascade write throws `InvalidOperationException` naming the missing reference.
2. Every repository a cascade can reach is created on the same `Database` (step 7). A missing one throws
   `InvalidOperationException` naming it.
3. Choose `cascadeDelete: true` when the children belong to the parent: they are deleted first when the parent is.
   Leave it `false` for a *view* (the sales of a product); the foreign key then refuses to delete a parent that still
   has children.

What is written then follows the call:

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Home/Calls/PurchaseCreate.cs (shortened)
var purchase = new Purchase
{
    User = new User { Id = userId },
    BuyDate = buyDate,
    Items =
    [
        .. lines.Select(l => new PurchaseItem
        {
            Product = new Product { Id = l.ProductId },
            Price = l.Price,
            Amount = l.Quantity,
        }),
    ],
};

if (!await repositories.Purchases.Insert(purchase, cancellationToken: cancellationToken)) // the purchase, then each item
{
    throw new InvalidOperationException("The purchase was not recorded.");
}
```

- `Insert` without a projection inserts every collection that holds a non-empty list. References are written only as
  their foreign key.
- With a projection, only what it marks is written. A reference marked beyond its key is also inserted or updated.
- `Update(newBean, oldBean, projection)` with `Items = Mark.List(shape)` treats the list as the full state: children
  without a key are inserted, known ones updated in the fields the shape marks, missing ones deleted. A collection the
  projection does not mark is left alone.

## 7. Create the database and the repositories

Create one `Database` per database and schema, then every repository on it:

```csharp
// samples/Shopping/Shopping.Persistence/ShoppingStore.cs (shortened)
switch (provider)
{
    case DatabaseProvider.PostgreSql:
        var dataSource = NpgsqlDataSource.Create(connectionString);
        return new ShoppingStore(provider, PostgreSql.CreateDatabase(dataSource, schema, logger), dataSource);
    case DatabaseProvider.SqlServer:
        return new ShoppingStore(provider, SqlServer.CreateDatabase(connectionString, schema, logger), null);
}

// in the constructor:
Repositories = new ShoppingRepositories(
    new UserRepository(database),
    new ProductRepository(database),
    new PurchaseRepository(database),
    new PurchaseItemRepository(database),
    new PurchaseAmendmentRepository(database),
    new PurchaseAmendmentLineRepository(database),
    database);
```

- `PostgreSql.CreateDatabase(dataSource, schema, logger)` (namespace `Wdc.Persistence.PostgreSql`) takes an
  `NpgsqlDataSource` you own: dispose it when the application stops, as `ShoppingStore.DisposeAsync` does.
- `SqlServer.CreateDatabase(connectionString, schema, logger)` (namespace `Wdc.Persistence.SqlServer`) uses SqlClient's
  pool. The dialect targets SQL Server 2025 (`JSON_OBJECT`, `JSON_ARRAYAGG`).
- `schema` qualifies every table of the generated SQL; leave it `null` to use the connection's default.
- The connection string comes from configuration (`Shopping:Database:ConnectionString` in the sample), never from
  source code.
- Repositories are stateless apart from the `Database`: create them once and share them.

## 8. Use them in transactions

`Database` is also the `ITransactionService`. Register both as singletons on a server, and group writes that belong
together in `Required`:

```csharp
// samples/Shopping/Shopping.Persistence/ShoppingDemoData.cs (shortened)
await repositories.Transactions.Required(async _ =>
{
    var admin = await NewUser(repositories, "admin", "admin", "John Smith", "Admin", cancellationToken);
    // ... products ...
    await repositories.Purchases.Insert(new Purchase
    {
        User = admin,
        BuyDate = new DateTimeOffset(2010, 1, 1, 14, 30, 0, TimeSpan.Zero),
        Items = [new PurchaseItem { Product = coffee, Amount = 1, Price = 200.00m }],
    }, cancellationToken: cancellationToken);
}, cancellationToken);
```

- Repositories join the transaction open in the calling flow; a single write called outside one runs in its own, so
  an insert with its children is always atomic.
- Run the statements of one transaction one after the other, never with `Task.WhenAll`: a transaction is one
  connection.
- What a session receives is the protected version of these repositories, not the raw ones; see
  [Writing roles and rules](roles-and-rules.md).

## Checklist

- [ ] The entity's model, criteria, ordering enum and repository interface are in the domain project.
- [ ] The table exists in each dialect you support, with lower-case names and an index on each collection's foreign key.
- [ ] Every field to store is mapped; the key says whether it is `generated`.
- [ ] `Conditions` translates every criterion the class declares, virtual ones included.
- [ ] Every ordering name maps to columns.
- [ ] Each collection written in cascade has its back reference on the same column, and `cascadeDelete` says whether
      the children belong to the parent.
- [ ] The repository is created on the same `Database` as every repository its cascades reach.

## See also

- [Persistence](../concepts/persistence.md): reads in one statement, writes in cascade, dialects, the cascade guard.
- [Domain](../concepts/domain.md): models, projections, criteria and the repository contract.
- [Transactions](../concepts/transactions.md): propagations, rollback rules, detached transactions.
- [Writing roles and rules](roles-and-rules.md): protecting the new repository.
- [Exposing repositories over HTTP](rest.md): serving the repository to clients.
- [The Shopping sample](../samples/shopping.md)
