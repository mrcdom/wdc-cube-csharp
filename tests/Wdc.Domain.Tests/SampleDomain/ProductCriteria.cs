namespace Wdc.Domain.Tests;

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
