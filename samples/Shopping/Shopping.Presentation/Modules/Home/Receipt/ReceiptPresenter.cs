using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Receipt.Calls;
using Wdc.Cube;

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
        var purchase = await new ReceiptQuery(App.Repositories, purchaseId: purchaseId, userId: subject.Id).Run();

        var items = purchase.Items ?? [];
        _purchaseId = purchaseId;
        Scope.DateLabel = purchase.BuyDate is { } date ? Formats.Date(date) : string.Empty;
        Scope.Lines = [.. items.Select(i => new ReceiptLine(i.Product?.Name ?? string.Empty, i.Amount ?? 0, Formats.Money(i.Price ?? 0)))];
        Scope.TotalLabel = Formats.Money(items.Sum(i => (i.Price ?? 0) * (i.Amount ?? 0)));
        Scope.Corrections = [.. (purchase.Amendments ?? []).Select(a =>
            $"Purchase corrected on {(a.Date is { } when ? Formats.Date(when) : string.Empty)}: {a.Reason}")];
    }
}
