using Shopping.Domain.Purchases;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home.Purchases.Calls;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Purchases;

/// <summary>
/// A child of Home (no place): the user's purchases, most recent first, a page at a time. The page is Home's
/// parameter (in the address); the page size is fixed here.
/// </summary>
public sealed class PurchasesPanelPresenter : Presenter<RootPresenter, PurchasesPanelScope>
{
    public const int PageSize = 5;

    private int _pending;
    private readonly Func<long, Task> _openReceipt;
    private int _loadedPage;

    public PurchasesPanelPresenter(RootPresenter app, Func<long, Task> openReceipt)
        : base(app, new PurchasesPanelScope())
    {
        _openReceipt = openReceipt;
        Scope.OnOpenReceipt = Action<long>(OnOpenReceipt);
        Scope.OnPrevious = Action(OnPrevious);
        Scope.OnNext = Action(OnNext);
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
            var result = await new MyPurchasesPageQuery(App.Repositories, userId: subject.Id, page: page, pageSize: PageSize).Run();
            if (token != _pending || IsReleased)
            {
                return;
            }

            Page = result.Page;
            _loadedPage = result.Page;
            Scope.Purchases = [.. result.Purchases.Select(ToCard)];
            Scope.CountLabel = $"{result.Total} {(result.Total == 1 ? "purchase" : "purchases")}";
            Scope.PageLabel = $"{result.Page} / {result.TotalPages}";
            Scope.HasPrevious = result.Page > 1;
            Scope.HasNext = result.Page < result.TotalPages;
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

    private Task OnOpenReceipt(long purchaseId) => _openReceipt(purchaseId); // the owner decides where it goes

    private Task OnPrevious() => ShowPage(Page - 1);

    private Task OnNext() => ShowPage(Page + 1);
}
