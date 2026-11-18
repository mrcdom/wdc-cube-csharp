using Wdc.Domain;

namespace Shopping.Domain.Purchases;

/// <summary>Purchases. An insert writes the purchase and its items together.</summary>
public interface IPurchaseRepository : IRepository<Purchase, PurchaseCriteria, long>;
