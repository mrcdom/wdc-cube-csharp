using Shopping.Domain.Products;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public sealed class Purchase : IKeyedEntity
{
    public static readonly EntityModel<Purchase> Model = EntityModel.For(() => new Purchase())
        .Key(ValueKinds.Int64, "id", p => p.Id, (p, v) => p.Id = v)
        .Field(ValueKinds.Instant, "buyDate", p => p.BuyDate, (p, v) => p.BuyDate = v)
        .Reference("user", p => p.User, (p, v) => p.User = v, () => User.Model)
        .Collection("items", p => p.Items, (p, v) => p.Items = v, () => PurchaseItem.Model, () => new PurchaseItemCriteria())
        .Build();

    public long? Id { get; set; }

    public DateTimeOffset? BuyDate { get; set; }

    public User? User { get; set; }

    public List<PurchaseItem>? Items { get; set; }

    object? IKeyedEntity.Key => Id;
}

public sealed class PurchaseItem : IKeyedEntity
{
    public static readonly EntityModel<PurchaseItem> Model = EntityModel.For(() => new PurchaseItem())
        .Key(ValueKinds.Int64, "id", i => i.Id, (i, v) => i.Id = v)
        .Field(ValueKinds.Int32, "amount", i => i.Amount, (i, v) => i.Amount = v)
        .Field(ValueKinds.Decimal, "price", i => i.Price, (i, v) => i.Price = v)
        .Reference("purchase", i => i.Purchase, (i, v) => i.Purchase = v, () => Purchase.Model)
        .Reference("product", i => i.Product, (i, v) => i.Product = v, () => Product.Model)
        .Build();

    public long? Id { get; set; }

    public int? Amount { get; set; }

    /// <summary>The unit price when bought.</summary>
    public decimal? Price { get; set; }

    public Purchase? Purchase { get; set; }

    public Product? Product { get; set; }

    object? IKeyedEntity.Key => Id;
}

public enum PurchaseOrderBy
{
    OldestFirst,
    NewestFirst,
    MostRecentPurchaseFirst,
    EarliestPurchaseFirst,
}

public sealed class PurchaseCriteria : Criteria<PurchaseCriteria, Purchase, PurchaseOrderBy>
{
    public PurchaseCriteria()
    {
        PurchaseId = Comparable(ValueKinds.Int64, "purchaseId");
        UserId = Equality(ValueKinds.Int64, "userId");
        BuyDate = Comparable(ValueKinds.Instant, "buyDate");
        ProductId = Equality(ValueKinds.Int64, "productId");
    }

    public ComparableCriterion<PurchaseCriteria, long?> PurchaseId { get; }

    public Criterion<PurchaseCriteria, long?> UserId { get; }

    public ComparableCriterion<PurchaseCriteria, DateTimeOffset?> BuyDate { get; }

    /// <summary>Purchases that contain the product (a field of the items, not of the purchase).</summary>
    public Criterion<PurchaseCriteria, long?> ProductId { get; }
}

public enum PurchaseItemOrderBy
{
    OldestFirst,
    NewestFirst,
    MostExpensiveFirst,
    LargestQuantityFirst,
}

public sealed class PurchaseItemCriteria : Criteria<PurchaseItemCriteria, PurchaseItem, PurchaseItemOrderBy>
{
    public PurchaseItemCriteria()
    {
        PurchaseItemId = Comparable(ValueKinds.Int64, "purchaseItemId");
        PurchaseId = Equality(ValueKinds.Int64, "purchaseId");
        ProductId = Equality(ValueKinds.Int64, "productId");
        Amount = Comparable(ValueKinds.Int32, "amount");
        Price = Comparable(ValueKinds.Decimal, "price");
    }

    public ComparableCriterion<PurchaseItemCriteria, long?> PurchaseItemId { get; }

    public Criterion<PurchaseItemCriteria, long?> PurchaseId { get; }

    public Criterion<PurchaseItemCriteria, long?> ProductId { get; }

    public ComparableCriterion<PurchaseItemCriteria, int?> Amount { get; }

    public ComparableCriterion<PurchaseItemCriteria, decimal?> Price { get; }
}

/// <summary>Purchases. An insert writes the purchase and its items together.</summary>
public interface IPurchaseRepository : IRepository<Purchase, PurchaseCriteria, long>;

public interface IPurchaseItemRepository : IRepository<PurchaseItem, PurchaseItemCriteria, long>;
