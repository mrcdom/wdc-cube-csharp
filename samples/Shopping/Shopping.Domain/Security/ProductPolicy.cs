using Shopping.Domain.Products;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The products: the catalogue every role reads; Super, Admin and Manager maintain it.</summary>
public static class ProductPolicy
{
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
            .Grant([.. ShoppingPolicies.Super, .. ShoppingPolicies.Admin, .. ShoppingPolicies.Manager], g => g
                .Read(s_fields).Create(s_fields).Update(s_fields).Delete())
            .Grant(ShoppingPolicies.Customer, g => g.Read(s_fields))
            .Build();
}
