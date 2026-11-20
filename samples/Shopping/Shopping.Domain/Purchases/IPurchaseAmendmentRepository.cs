using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public interface IPurchaseAmendmentRepository : IRepository<PurchaseAmendment, PurchaseAmendmentCriteria, long>;
