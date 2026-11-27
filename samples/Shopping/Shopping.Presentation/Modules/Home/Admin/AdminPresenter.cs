using Shopping.Domain.Purchases;
using Shopping.I18n;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Shopping.Presentation.Modules.Home.Admin.Calls;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Admin;

/// <summary>
/// The administration, under Home: the purchases of every customer, a page at a time, filtered by the customer's
/// name; opening one corrects it. Offered only to whoever may correct; the repositories refuse the
/// others anyway.
/// </summary>
public sealed class AdminPresenter(RootPresenter app) : CubePresenter<RootPresenter, AdminScope>(app, new AdminScope())
{
    public const int PageSize = 10;

    /// <summary>The permission the administration needs.</summary>
    public const string Permission = "purchase-amendment:write";

    public static Message NotAllowed => Texts.AdminNotAllowed();

    private int _pending;
    private (int Page, string? Customer)? _loaded;

    /// <summary>The page shown (1-based).</summary>
    public int Page { get; private set; } = 1;

    public string? Customer { get; private set; }

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        if (App.Subject?.Can(Permission) != true)
        {
            App.ShowError(NotAllowed);
            await App.FlipToIntent(intent.Redirect(HomeKeys.Place));
            return false;
        }

        var keys = new AdminKeys(App, intent);
        keys.ParentSlot(Scope); // into Home, before any wait
        if (initialization)
        {
            Scope.OnFilter = Action<string>(OnFilter);
            Scope.OnPrevious = Action(OnPrevious);
            Scope.OnNext = Action(OnNext);
            Scope.OnOpenPurchase = Action<long>(OnOpenPurchase);
            Scope.OnBack = Action(OnBack);
        }

        if (last)
        {
            Scope.Content = null; // the list
        }
        else
        {
            keys.ParentSlot = content => Scope.Content = content;
            keys.Admin = this;
        }

        await ShowPage(keys.ListPage, keys.Customer);
        return true;
    }

    public override void PublishParameters(FlipIntent intent)
    {
        var keys = new AdminKeys(App, intent);
        keys.ListPage = Page;
        keys.Customer = Customer;
    }

    /// <summary>The list again, as it is now (after a correction).</summary>
    public Task Reload() => ShowPage(Page, Customer, reload: true);

    private async Task ShowPage(int page, string? customer, bool reload = false)
    {
        page = Math.Max(1, page);
        customer = string.IsNullOrWhiteSpace(customer) ? null : customer.Trim();
        if (!reload && _loaded == (page, customer))
        {
            return;
        }

        Page = page;
        Customer = customer;
        Scope.CustomerFilter = customer ?? string.Empty;
        var token = ++_pending;
        Scope.Loading = true;
        try
        {
            var result = await new PurchasesPageQuery(App.Repositories, customer, page: page, pageSize: PageSize).Run();
            if (token != _pending || IsReleased)
            {
                return;
            }

            Page = result.Page;
            _loaded = (result.Page, customer);
            Scope.Purchases = [.. result.Purchases.Select(ToRow)];
            Scope.CountLabel = Texts.CommonPurchaseCount(count: result.Total);
            Scope.PageLabel = Texts.CommonPageOf(page: result.Page, pages: result.TotalPages);
            Scope.HasPrevious = result.Page > 1;
            Scope.HasNext = result.Page < result.TotalPages;
        }
        catch (Exception caught) when (caught is not OperationCanceledException)
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

    private Task OnFilter(string customer) => ShowPage(1, customer);

    private Task OnPrevious() => ShowPage(Page - 1, Customer);

    private Task OnNext() => ShowPage(Page + 1, Customer);

    private Task OnBack() => new HomeKeys(App).Flip();

    private async Task OnOpenPurchase(long purchaseId)
    {
        try
        {
            await new AmendKeys(App) { PurchaseId = purchaseId, ListPage = Page, Customer = Customer }.Flip();
        }
        catch (PurchaseNotFoundException)
        {
            App.ShowError(HomePresenter.ReceiptNotFound);
        }
    }

    private static AdminPurchaseRow ToRow(Purchase p)
    {
        var items = p.Items ?? [];
        return new AdminPurchaseRow(
            p.Id!.Value,
            Texts.AdminPurchaseNumber(id: p.Id!.Value),
            p.User?.Name ?? string.Empty,
            p.BuyDate is { } date ? Formats.Date(date) : null,
            string.Join(", ", items.Select(i => i.Product?.Name).Where(n => !string.IsNullOrWhiteSpace(n))),
            Formats.Money(items.Sum(i => (i.Price ?? 0) * (i.Amount ?? 0))),
            p.Amendments is { Count: > 0 });
    }
}
