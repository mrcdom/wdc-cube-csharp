using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

public sealed class ProductRepository(Database database)
    : SqlRepository<Product, ProductCriteria, long>(database, ShoppingMappings.Product), IProductRepository
{
    public async Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        var product = await FetchById(productId, new Product { Image = Mark.Bytes }, cancellationToken)
                      ?? throw new KeyNotFoundException($"No product {productId}.");
        return product.Image;
    }
}

public sealed class UserRepository(Database database)
    : SqlRepository<User, UserCriteria, long>(database, ShoppingMappings.User), IUserRepository;

public sealed class PurchaseItemRepository(Database database)
    : SqlRepository<PurchaseItem, PurchaseItemCriteria, long>(database, ShoppingMappings.PurchaseItem), IPurchaseItemRepository
{
    public override Task<bool> Insert(PurchaseItem bean, PurchaseItem? projection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        if (bean.Amount is not > 0)
        {
            throw new InvalidRequestException("the item's amount must be greater than zero");
        }

        return base.Insert(bean, projection, cancellationToken);
    }
}

/// <summary>A purchase is written with its items, in one transaction (the caller's, or one of its own).</summary>
public sealed class PurchaseRepository(Database database, PurchaseItemRepository items)
    : SqlRepository<Purchase, PurchaseCriteria, long>(database, ShoppingMappings.Purchase), IPurchaseRepository
{
    public override Task<bool> Insert(Purchase bean, Purchase? projection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        return Database.Required(async _ =>
        {
            if (!await base.Insert(bean, projection, cancellationToken))
            {
                return false;
            }

            foreach (var item in bean.Items ?? [])
            {
                item.Purchase = bean;
                await items.Insert(item, cancellationToken: cancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    public override async Task<int> Delete(PurchaseCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (!criteria.HasFilter)
        {
            throw new InvalidRequestException("delete of purchases without a filter is refused");
        }

        // Children first, in the same transaction.
        return await Database.Required(async _ =>
        {
            var ids = (await Fetch(criteria.WithProjection(Purchase.Model.KeyProjection()), cancellationToken: cancellationToken))
                .Select(p => p.Id).ToList();
            if (ids.Count == 0)
            {
                return 0;
            }

            await items.Delete(new PurchaseItemCriteria().PurchaseId.In(ids), cancellationToken);
            return await base.Delete(new PurchaseCriteria().PurchaseId.In(ids), cancellationToken);
        }, cancellationToken);
    }
}
