using Shopping.Domain.Purchases;
using Wdc.Rest.Client;

namespace Shopping.Client;

public sealed class RemotePurchaseAmendmentRepository(RestClient client)
    : RemoteRepository<PurchaseAmendment, PurchaseAmendmentCriteria, long>(client, "/api/repo/purchase-amendment",
        PurchaseAmendment.Model, () => new PurchaseAmendmentCriteria()), IPurchaseAmendmentRepository;
