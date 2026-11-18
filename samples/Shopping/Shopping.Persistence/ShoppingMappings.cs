using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>How the Shopping entities are stored: written once, for every database.</summary>
public static class ShoppingMappings
{
    public static readonly EntityMapping<Product> Product = EntityMapping.For(Domain.Products.Product.Model, "product", "pr")
        .Key("id", generated: true)
        .Column("name")
        .Column("price")
        .Column("description")
        .Column("image")
        .Filter("productId", "id")
        .Ordering<ProductOrderBy>(o => o switch
        {
            ProductOrderBy.NewestFirst => [SortSpec.Desc("id")],
            ProductOrderBy.NameAToZ => [SortSpec.Asc("name")],
            ProductOrderBy.CheapestFirst => [SortSpec.Asc("price")],
            ProductOrderBy.MostExpensiveFirst => [SortSpec.Desc("price")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    public static readonly EntityMapping<User> User = EntityMapping.For(Domain.Users.User.Model, "user_account", "us")
        .Key("id", generated: true)
        .Column("userName", "user_name")
        .Column("password")
        .Column("name")
        .Column("roles")
        .Filter("userId", "id")
        .Ordering<UserOrderBy>(o => o switch
        {
            UserOrderBy.NewestFirst => [SortSpec.Desc("id")],
            UserOrderBy.NameAToZ => [SortSpec.Asc("name")],
            UserOrderBy.LoginAToZ => [SortSpec.Asc("user_name")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    public static readonly EntityMapping<Purchase> Purchase = EntityMapping.For(Domain.Purchases.Purchase.Model, "purchase", "pu")
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
        .Ordering<PurchaseOrderBy>(o => o switch
        {
            PurchaseOrderBy.NewestFirst => [SortSpec.Desc("id")],
            PurchaseOrderBy.MostRecentPurchaseFirst => [SortSpec.Desc("buy_date"), SortSpec.Desc("id")],
            PurchaseOrderBy.EarliestPurchaseFirst => [SortSpec.Asc("buy_date")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    public static readonly EntityMapping<PurchaseItem> PurchaseItem = EntityMapping.For(Domain.Purchases.PurchaseItem.Model, "purchase_item", "pi")
        .Key("id", generated: true)
        .Column("amount")
        .Column("price")
        .Reference("purchase", "purchase_id", () => Purchase)
        .Reference("product", "product_id", () => Product)
        .Filter("purchaseItemId", "id")
        .Filter("purchaseId", "purchase_id")
        .Filter("productId", "product_id")
        .Ordering<PurchaseItemOrderBy>(o => o switch
        {
            PurchaseItemOrderBy.NewestFirst => [SortSpec.Desc("id")],
            PurchaseItemOrderBy.MostExpensiveFirst => [SortSpec.Desc("price")],
            PurchaseItemOrderBy.LargestQuantityFirst => [SortSpec.Desc("amount")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();
}
