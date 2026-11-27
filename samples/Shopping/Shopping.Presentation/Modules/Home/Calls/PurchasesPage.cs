using Shopping.Domain.Purchases;

namespace Shopping.Presentation.Modules.Home.Calls;

/// <summary>One page of purchases, the page clamped to the ones that exist.</summary>
public sealed record PurchasesPage(int Total, int Page, int TotalPages, IReadOnlyList<Purchase> Purchases)
{
    /// <summary>Counts first and clamps the page, then fetches the page that exists.</summary>
    internal static async Task<PurchasesPage> Load(IPurchaseRepository purchases,
        PurchaseCriteria criteria, Purchase projection, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await purchases.Count(criteria, cancellationToken);
        var totalPages = Math.Max(1, (total + pageSize - 1) / pageSize);
        page = Math.Min(page, totalPages);
        criteria.WithProjection(projection);
        IReadOnlyList<Purchase> rows = total == 0
            ? []
            : await purchases.Fetch(criteria, (page - 1) * pageSize, pageSize, cancellationToken);
        return new PurchasesPage(total, page, totalPages, rows);
    }
}
