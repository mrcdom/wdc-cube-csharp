using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

public sealed class UserCriteria : Criteria<UserCriteria, User>
{
    public UserCriteria()
    {
        UserName = Text("userName");
        Role = Equality(ValueKinds.Enum<Role>(), "role");
    }

    public TextCriterion<UserCriteria> UserName { get; }

    public Criterion<UserCriteria, Role?> Role { get; }
}

public enum PurchaseOrderBy
{
    MostRecentFirst,
}

public sealed class PurchaseCriteria : Criteria<PurchaseCriteria, Purchase, PurchaseOrderBy>
{
    public PurchaseCriteria()
    {
        PurchaseId = Comparable(ValueKinds.Int64, "purchaseId");
        UserId = Equality(ValueKinds.Int64, "userId");
        ProductId = Equality(ValueKinds.Int64, "productId"); // virtual: purchases that contain the product
        BuyDate = Comparable(ValueKinds.Instant, "buyDate");
    }

    public ComparableCriterion<PurchaseCriteria, long?> PurchaseId { get; }

    public Criterion<PurchaseCriteria, long?> UserId { get; }

    public Criterion<PurchaseCriteria, long?> ProductId { get; }

    public ComparableCriterion<PurchaseCriteria, DateTimeOffset?> BuyDate { get; }
}

/// <summary>The mappings of the Shopping domain, written once for every dialect.</summary>
public static class ShoppingMappings
{
    public static readonly EntityMapping<Product> Product = EntityMapping.For(Domain.Tests.Product.Model, "product", "p")
        .Key("id", generated: true)
        .Column("name")
        .Column("price")
        .Column("description")
        .Column("image")
        .Filter("productId", "id")
        .Ordering<ProductOrderBy>(o => o switch
        {
            ProductOrderBy.NameAToZ => [SortSpec.Asc("name")],
            ProductOrderBy.CheapestFirst => [SortSpec.Asc("price")],
            _ => [],
        })
        .Build();

    public static readonly EntityMapping<User> User = EntityMapping.For(Domain.Tests.User.Model, "user_account", "u")
        .Key("id", generated: true)
        .Column("userName", "user_name")
        .Column("password")
        .Column("name")
        .Column("role")
        .Build();

    public static readonly EntityMapping<Purchase> Purchase = EntityMapping.For(Domain.Tests.Purchase.Model, "purchase", "pu")
        .Key("id", generated: true)
        .Column("buyDate", "buy_date")
        .Reference("user", "user_id", () => User)
        .Collection("items", "purchase_id", () => PurchaseItem!) // declared below: mutual reference, resolved lazily
        .Filter("purchaseId", "id")
        .Filter("userId", "user_id")
        .Filter("productId", (criterion, ctx) => Sql.Exists(w =>
        {
            var item = w.NextAlias("pi");
            w.Append("SELECT 1 FROM ").Table("purchase_item").Append(' ').Identifier(item).Append(" WHERE ");
            Sql.And([
                Sql.Equal(Sql.Column(item, "purchase_id"), ctx.Column("id")),
                Sql.Translate(Sql.Column(item, "product_id"), criterion),
            ])!.Render(w);
        }))
        .Ordering<PurchaseOrderBy>(o => [SortSpec.Desc("buy_date")])
        .Build();

    public static readonly EntityMapping<PurchaseItem> PurchaseItem = EntityMapping.For(Domain.Tests.PurchaseItem.Model, "purchase_item", "pi")
        .Key("id", generated: true)
        .Column("amount")
        .Column("price")
        .Reference("purchase", "purchase_id", () => Purchase)
        .Reference("product", "product_id", () => Product)
        .Filter("purchaseId", "purchase_id")
        .Ordering<PurchaseItemOrderBy>(o => [SortSpec.Desc("price")])
        .Build();
}

/// <summary>The four repositories over one database.</summary>
public sealed class Shopping(Database database)
{
    public Database Database { get; } = database;

    public SqlRepository<Product, ProductCriteria, long> Products { get; } = new(database, ShoppingMappings.Product);

    public SqlRepository<User, UserCriteria, long> Users { get; } = new(database, ShoppingMappings.User);

    public SqlRepository<Purchase, PurchaseCriteria, long> Purchases { get; } = new(database, ShoppingMappings.Purchase);

    public SqlRepository<PurchaseItem, PurchaseItemCriteria, long> Items { get; } = new(database, ShoppingMappings.PurchaseItem);
}
