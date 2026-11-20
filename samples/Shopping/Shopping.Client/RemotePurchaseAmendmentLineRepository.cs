using Shopping.Domain.Purchases;
using Wdc.Rest.Client;

namespace Shopping.Client;

public sealed class RemotePurchaseAmendmentLineRepository(RestClient client)
    : RemoteRepository<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria, long>(client, "/api/repo/purchase-amendment-line",
        PurchaseAmendmentLine.Model, () => new PurchaseAmendmentLineCriteria()), IPurchaseAmendmentLineRepository;
