using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public interface IPurchaseAmendmentLineRepository : IRepository<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria, long>;
