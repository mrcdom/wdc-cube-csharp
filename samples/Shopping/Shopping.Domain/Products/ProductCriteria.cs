using Wdc.Domain;

namespace Shopping.Domain.Products;

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
