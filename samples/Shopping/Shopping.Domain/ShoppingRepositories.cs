using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain;

/// <summary>The repositories and the transaction service, as one thing the presentation receives.</summary>
public sealed record ShoppingRepositories(
    IUserRepository Users,
    IProductRepository Products,
    IPurchaseRepository Purchases,
    IPurchaseItemRepository PurchaseItems,
    IPurchaseAmendmentRepository Amendments,
    IPurchaseAmendmentLineRepository AmendmentLines,
    ITransactionService Transactions);
