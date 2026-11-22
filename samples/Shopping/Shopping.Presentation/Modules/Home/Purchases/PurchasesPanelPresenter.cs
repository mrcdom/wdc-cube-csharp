using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Presentation.Infra;
using Wdc.Cube;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Purchases;

/// <summary>
/// A child of Home (no place): the user's purchases, most recent first, a page at a time. The page is Home's
/// parameter (in the address); the page size is fixed here.
/// </summary>
public sealed class PurchasesPanelPresenter : Presenter<RootPresenter, PurchasesPanelScope>
{
    public const int PageSize = 5;

    private int _pending;
    private int _loadedPage;

    public PurchasesPanelPresenter(RootPresenter app, Func<long, Task> openReceipt)
        : base(app, new PurchasesPanelScope())
    {
        Scope.OnOpenReceipt = Action<long>(openReceipt);
        Scope.OnPrevious = Action(() => ShowPage(Page - 1));
        Scope.OnNext = Action(() => ShowPage(Page + 1));
    }

    /// <summary>The page shown (1-based).</summary>
    public int Page { get; private set; } = 1;

    /// <summary>Shows <paramref name="page"/>; fetches only when it is not the one already on screen.</summary>
    public async Task ShowPage(int page, bool reload = false)
    {
        page = Math.Max(1, page);
        if (!reload && page == _loadedPage)
        {
            return;
        }

        Page = page;
        var token = ++_pending;
        Scope.Loading = true;
        try
        {
            var subject = App.Subject ?? throw new InvalidOperationException("No session.");
            var criteria = new PurchaseCriteria().UserId.Eq(subject.Id).WithOrderBy(PurchaseOrderBy.MostRecentPurchaseFirst);

            // Count first and clamp, then fetch the page that exists.
            var total = await App.Repositories.Purchases.Count(criteria);
            var totalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Math.Min(page, totalPages);

            criteria.WithProjection(new Purchase
            {
                Id = Mark.Int64,
                BuyDate = Mark.Instant,
                Items = Mark.List(new PurchaseItem { Price = Mark.Decimal, Amount = Mark.Int32, Product = new Product { Name = Mark.String } }),
            });
            var purchases = total == 0 ? [] : await App.Repositories.Purchases.Fetch(criteria, (page - 1) * PageSize, PageSize);
            if (token != _pending || IsReleased)
            {
                return;
            }

            Page = page;
            _loadedPage = page;
            Scope.Purchases = [.. purchases.Select(ToCard)];
            Scope.CountLabel = $"{total} {(total == 1 ? "purchase" : "purchases")}";
            Scope.PageLabel = $"{page} / {totalPages}";
            Scope.HasPrevious = page > 1;
            Scope.HasNext = page < totalPages;
        }
        catch (Exception caught)
        {
            App.Unexpected("Loading the purchases", caught);
        }
        finally
        {
            if (token == _pending && !IsReleased)
            {
                Scope.Loading = false;
            }
        }
    }

    private static PurchaseCard ToCard(Purchase p)
    {
        var items = p.Items ?? [];
        return new PurchaseCard(
            p.Id!.Value,
            p.BuyDate is { } date ? Formats.Date(date) : string.Empty,
            string.Join(", ", items.Select(i => i.Product?.Name).Where(n => !string.IsNullOrWhiteSpace(n))),
            Formats.Money(items.Sum(i => (i.Price ?? 0) * (i.Amount ?? 0))));
    }
}
