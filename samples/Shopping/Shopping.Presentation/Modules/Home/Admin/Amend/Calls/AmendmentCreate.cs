using Shopping.Domain;
using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Admin.Amend.Calls;

/// <summary>Records a correction of a purchase, with its lines, in one transaction.</summary>
/// <returns>Whether it was recorded.</returns>
/// <exception cref="InvalidRequestException">A line's quantity before differs from the stored one.</exception>
public sealed class AmendmentCreate(ShoppingRepositories repositories, PurchaseAmendment errata)
    : ResourceCreate<bool>(repositories.Transactions)
{
    protected override Task<bool> Create(CancellationToken cancellationToken) =>
        repositories.Amendments.Insert(errata, cancellationToken: cancellationToken);
}
