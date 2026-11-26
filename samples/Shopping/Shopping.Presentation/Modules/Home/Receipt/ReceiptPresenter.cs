using Shopping.Domain.Purchases;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home;
using Wdc.Cube;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Receipt;

/// <summary>
/// A receipt, under Home. Only the logged-in user's purchases: someone else's is the same as one that does not exist.
/// </summary>
public sealed class ReceiptPresenter(RootPresenter app) : CubePresenter<RootPresenter, ReceiptScope>(app, new ReceiptScope())
{
    private long? _purchaseId;

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new ReceiptKeys(App, intent);
        if (keys.PurchaseId is not { } purchaseId)
        {
            await App.FlipToIntent(intent.Redirect(HomeKeys.Place));
            return false;
        }

        if (initialization)
        {
            Scope.OnContinue = Action(() => new HomeKeys(App).Flip());
        }

        Scope.NotifySuccess = keys.PurchaseMade; // the attribute only comes with the flip from the cart
        if (purchaseId != _purchaseId)
        {
            await Load(purchaseId);
        }

        keys.ParentSlot(Scope);
        return true;
    }

    public override void PublishParameters(FlipIntent intent) => new ReceiptKeys(App, intent).PurchaseId = _purchaseId;

    private async Task Load(long purchaseId)
    {
        var subject = App.Subject ?? throw new InvalidOperationException("No session.");
        var criteria = new PurchaseCriteria()
            .PurchaseId.Eq(purchaseId)
            .UserId.Eq(subject.Id) // ownership: never only by key
            .WithProjection(new Purchase
            {
                Id = Mark.Int64,
                BuyDate = Mark.Instant,
                Items = Mark.List(
                    new PurchaseItem { Id = Mark.Int64, Price = Mark.Decimal, Amount = Mark.Int32, Product = new Domain.Products.Product { Name = Mark.String } },
                    new PurchaseItemCriteria().WithOrderBy(PurchaseItemOrderBy.OldestFirst)),
                Amendments = Mark.List(
                    new PurchaseAmendment { Date = Mark.Instant, Reason = Mark.String },
                    new PurchaseAmendmentCriteria().WithOrderBy(PurchaseAmendmentOrderBy.OldestFirst)),
            });
        var purchase = (await App.Repositories.Purchases.Fetch(criteria, limit: 1)).FirstOrDefault()
                       ?? throw new PurchaseNotFoundException(purchaseId);

        var items = purchase.Items ?? [];
        _purchaseId = purchaseId;
        Scope.DateLabel = purchase.BuyDate is { } date ? Formats.Date(date) : string.Empty;
        Scope.Lines = [.. items.Select(i => new ReceiptLine(i.Product?.Name ?? string.Empty, i.Amount ?? 0, Formats.Money(i.Price ?? 0)))];
        Scope.TotalLabel = Formats.Money(items.Sum(i => (i.Price ?? 0) * (i.Amount ?? 0)));
        Scope.Corrections = [.. (purchase.Amendments ?? []).Select(a =>
            $"Purchase corrected on {(a.Date is { } when ? Formats.Date(when) : string.Empty)}: {a.Reason}")];
    }
}
