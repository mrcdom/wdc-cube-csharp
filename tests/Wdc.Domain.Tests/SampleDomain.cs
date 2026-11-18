namespace Wdc.Domain.Tests;

// The Shopping domain of the Kotlin version, declared the C# way.

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

public enum Role
{
    Customer,
    Admin,
}

public sealed class User : IKeyedEntity
{
    public static readonly EntityModel<User> Model = EntityModel.For(() => new User())
        .Key(ValueKinds.Int64, "id", u => u.Id, (u, v) => u.Id = v)
        .Field(ValueKinds.String, "userName", u => u.UserName, (u, v) => u.UserName = v)
        .Field(ValueKinds.String, "password", u => u.Password, (u, v) => u.Password = v, inDefaultProjection: false)
        .Field(ValueKinds.String, "name", u => u.Name, (u, v) => u.Name = v)
        .Field(ValueKinds.Enum<Role>(), "role", u => u.Role, (u, v) => u.Role = v)
        .Build();

    public long? Id { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string? Name { get; set; }

    public Role? Role { get; set; }

    object? IKeyedEntity.Key => Id;
}

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

    public decimal? Price { get; set; }

    public Purchase? Purchase { get; set; }

    public Product? Product { get; set; }

    object? IKeyedEntity.Key => Id;
}

public enum ProductOrderBy
{
    NameAToZ,
    CheapestFirst,
}

public sealed class ProductCriteria : Criteria<ProductCriteria, Product, ProductOrderBy>
{
    public ProductCriteria()
    {
        ProductId = Comparable(ValueKinds.Int64, "productId");
        Name = Text("name");
        Price = Comparable(ValueKinds.Decimal, "price");
    }

    public ComparableCriterion<ProductCriteria, long?> ProductId { get; }

    public TextCriterion<ProductCriteria> Name { get; }

    public ComparableCriterion<ProductCriteria, decimal?> Price { get; }
}

public enum PurchaseItemOrderBy
{
    MostExpensiveFirst,
}

public sealed class PurchaseItemCriteria : Criteria<PurchaseItemCriteria, PurchaseItem, PurchaseItemOrderBy>
{
    public PurchaseItemCriteria()
    {
        PurchaseId = Equality(ValueKinds.Int64, "purchaseId");
        Amount = Comparable(ValueKinds.Int32, "amount");
    }

    public Criterion<PurchaseItemCriteria, long?> PurchaseId { get; }

    public ComparableCriterion<PurchaseItemCriteria, int?> Amount { get; }
}
