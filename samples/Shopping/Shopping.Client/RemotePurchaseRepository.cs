using Shopping.Domain.Purchases;
using Wdc.Rest.Client;

namespace Shopping.Client;

public sealed class RemotePurchaseRepository(RestClient client)
    : RemoteRepository<Purchase, PurchaseCriteria, long>(client, "/api/repo/purchase", Purchase.Model, () => new PurchaseCriteria()),
        IPurchaseRepository;
