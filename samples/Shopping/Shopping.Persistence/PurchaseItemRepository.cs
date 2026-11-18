using Shopping.Domain.Purchases;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

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
