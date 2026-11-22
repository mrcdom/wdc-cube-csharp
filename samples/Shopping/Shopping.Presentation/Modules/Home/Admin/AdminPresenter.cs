using Shopping.Presentation.Modules.Home;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Wdc.Cube;
using Wdc.Domain;

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

    public const string NotAllowed = "Você não tem acesso à administração.";

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
            Scope.OnFilter = Action<string>(text => ShowPage(1, text));
            Scope.OnPrevious = Action(() => ShowPage(Page - 1, Customer));
            Scope.OnNext = Action(() => ShowPage(Page + 1, Customer));
            Scope.OnOpenPurchase = Action<long>(OpenPurchase);
            Scope.OnBack = Action(() => new HomeKeys(App).Flip());
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
            var criteria = new PurchaseCriteria().WithOrderBy(PurchaseOrderBy.MostRecentPurchaseFirst);
            if (customer is not null)
            {
                criteria.CustomerName.Containing(customer);
            }

            var total = await App.Repositories.Purchases.Count(criteria);
            var totalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Math.Min(page, totalPages);
            criteria.WithProjection(new Purchase
            {
                Id = Mark.Int64,
                BuyDate = Mark.Instant,
                User = new User { Name = Mark.String },
                Items = Mark.List(new PurchaseItem { Price = Mark.Decimal, Amount = Mark.Int32, Product = new Product { Name = Mark.String } }),
                Amendments = Mark.List(new PurchaseAmendment { Id = Mark.Int64 }),
            });
            var purchases = total == 0 ? [] : await App.Repositories.Purchases.Fetch(criteria, (page - 1) * PageSize, PageSize);
            if (token != _pending || IsReleased)
            {
                return;
            }

            Page = page;
            _loaded = (page, customer);
            Scope.Purchases = [.. purchases.Select(ToRow)];
            Scope.CountLabel = $"{total} {(total == 1 ? "compra" : "compras")}";
            Scope.PageLabel = $"{page} / {totalPages}";
            Scope.HasPrevious = page > 1;
            Scope.HasNext = page < totalPages;
        }
        catch (Exception caught) when (caught is not OperationCanceledException)
        {
            App.Unexpected("Carregando as compras", caught);
        }
        finally
        {
            if (token == _pending && !IsReleased)
            {
                Scope.Loading = false;
            }
        }
    }

    private async Task OpenPurchase(long purchaseId)
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
            $"Nº {p.Id}",
            p.User?.Name ?? string.Empty,
            p.BuyDate is { } date ? Formats.Date(date) : string.Empty,
            string.Join(", ", items.Select(i => i.Product?.Name).Where(n => !string.IsNullOrWhiteSpace(n))),
            Formats.Money(items.Sum(i => (i.Price ?? 0) * (i.Amount ?? 0))),
            p.Amendments is { Count: > 0 });
    }
}
