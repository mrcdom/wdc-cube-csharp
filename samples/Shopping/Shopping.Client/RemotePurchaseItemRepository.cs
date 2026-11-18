using Shopping.Domain.Purchases;
using Wdc.Rest.Client;

namespace Shopping.Client;

public sealed class RemotePurchaseItemRepository(RestClient client)
    : RemoteRepository<PurchaseItem, PurchaseItemCriteria, long>(client, "/api/repo/purchase-item", PurchaseItem.Model,
        () => new PurchaseItemCriteria()), IPurchaseItemRepository;
