using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public interface IPurchaseItemRepository : IRepository<PurchaseItem, PurchaseItemCriteria, long>;
