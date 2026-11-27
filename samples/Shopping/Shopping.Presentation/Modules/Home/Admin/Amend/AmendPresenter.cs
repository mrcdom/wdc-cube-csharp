using Shopping.Domain.Purchases;
using Shopping.I18n;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home.Admin;
using Shopping.Presentation.Modules.Home.Admin.Amend.Calls;
using Wdc.Cube;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Admin.Amend;

/// <summary>
/// Corrects a purchase already made: a smaller quantity per item (0 removes it), never all of them,
/// with a reason. The repository checks it all again; what it refuses here means the purchase changed meanwhile.
/// </summary>
public sealed class AmendPresenter(RootPresenter app) : CubePresenter<RootPresenter, AmendScope>(app, new AmendScope())
{
    public static Message NothingToCorrect => Texts.AmendNothingToCorrect();

    public static Message ReasonRequired => Texts.AmendReasonRequired();

    public static Message EveryItemRemoved => Texts.AmendEveryItemRemoved();

    public static Message OnlyDown => Texts.AmendOnlyDown();

    public static Message ChangedMeanwhile => Texts.AmendChangedMeanwhile();

    public static Message Saved => Texts.AmendSaved();


    private readonly Dictionary<long, decimal> _prices = [];
    private long? _purchaseId;
    private AdminPresenter? _admin;

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new AmendKeys(App, intent);
        _admin = keys.Admin;
        if (keys.PurchaseId is not { } purchaseId)
        {
            await App.FlipToIntent(intent.Redirect(AdminKeys.Place));
            return false;
        }

        if (initialization)
        {
            Scope.OnChangeReason = Action<string>(OnChangeReason);
            Scope.OnConfirm = Action(OnConfirm);
            Scope.OnBack = Action(OnBack);
        }

        if (purchaseId != _purchaseId)
        {
            await Load(purchaseId);
        }

        keys.ParentSlot(Scope);
        return true;
    }

    public override void PublishParameters(FlipIntent intent) => new AmendKeys(App, intent).PurchaseId = _purchaseId;

    private async Task Load(long purchaseId)
    {
        var purchase = await new AmendablePurchaseQuery(App.Repositories, purchaseId).Run();

        _purchaseId = purchaseId;
        Scope.TitleLabel = Texts.AmendTitle(id: purchaseId);
        Scope.CustomerLabel = purchase.User?.Name ?? string.Empty;
        Scope.DateLabel = purchase.BuyDate is { } date ? Formats.Date(date) : null;
        _prices.Clear();
        Scope.Lines.Assign([.. (purchase.Items ?? []).Select(NewLine)]);
        Scope.TotalLabel = Formats.Money(Total(l => l.Quantity));
        Scope.History = [.. (purchase.Amendments ?? []).Select(ToEntry)];
        Scope.Reason = string.Empty;
        Scope.ErrorMessage = null;
        Refresh();
    }

    private AmendLineScope NewLine(PurchaseItem item)
    {
        var line = new AmendLineScope
        {
            Identity = item.Id!.Value,
            ItemId = item.Id!.Value,
            Name = item.Product?.Name ?? string.Empty,
            PriceLabel = Formats.Money(item.Price ?? 0),
            Quantity = item.Amount ?? 0,
            NewQuantity = item.Amount ?? 0,
        };
        _prices[line.ItemId] = item.Price ?? 0;
        line.OnChangeNewQuantity = Action<int>(quantity => OnChangeNewQuantity(line, quantity));
        return line;
    }

    private void OnChangeNewQuantity(AmendLineScope line, int quantity)
    {
        if (quantity < 0 || quantity > line.Quantity)
        {
            Scope.ErrorMessage = OnlyDown;
            return;
        }

        line.NewQuantity = quantity;
        Scope.ErrorMessage = null;
        Refresh();
    }

    private void OnChangeReason(string reason)
    {
        Scope.Reason = reason;
        Refresh();
    }

    private decimal Total(Func<AmendLineScope, int> quantity) =>
        Scope.Lines.Sum(l => _prices.GetValueOrDefault(l.ItemId) * quantity(l));

    private static AmendmentEntry ToEntry(PurchaseAmendment a) => new(
        a.Date is { } date ? Formats.Date(date) : null,
        a.Author?.Name ?? string.Empty,
        a.Reason ?? string.Empty,
        [.. (a.Lines ?? []).Select(l => Texts.AmendHistoryLine(product: l.Product?.Name ?? string.Empty, before: l.AmountBefore ?? 0, after: l.AmountAfter ?? 0))]);

    /// <summary>What follows from the lines and the reason: the new total and whether to offer the confirmation.</summary>
    private void Refresh()
    {
        Scope.NewTotalLabel = Formats.Money(Total(l => l.NewQuantity));
        Scope.CanConfirm = Problem() is null;
    }

    /// <summary>Why the correction cannot be confirmed as it is, or <see langword="null"/>.</summary>
    private Message? Problem()
    {
        if (!Scope.Lines.Any(l => l.NewQuantity < l.Quantity))
        {
            return NothingToCorrect;
        }

        if (Scope.Lines.All(l => l.NewQuantity == 0))
        {
            return EveryItemRemoved;
        }

        return string.IsNullOrWhiteSpace(Scope.Reason) ? ReasonRequired : null;
    }

    private async Task OnConfirm()
    {
        if (Problem() is { } problem)
        {
            Scope.ErrorMessage = problem;
            return;
        }

        var purchaseId = _purchaseId!.Value;
        var errata = new PurchaseAmendment
        {
            Purchase = new Purchase { Id = purchaseId },
            Reason = Scope.Reason.Trim(),
            Lines = [.. Scope.Lines.Where(l => l.NewQuantity < l.Quantity).Select(l => new PurchaseAmendmentLine
            {
                ItemId = l.ItemId,
                AmountBefore = l.Quantity, // what was on screen: refused if someone corrected it meanwhile
                AmountAfter = l.NewQuantity,
            })],
        };
        Scope.Saving = true;
        Scope.ErrorMessage = null;
        Scope.SuccessMessage = null;
        try
        {
            await new AmendmentCreate(App.Repositories, errata).Run();
            await Load(purchaseId);
            Scope.SuccessMessage = Saved;
            _ = _admin?.Reload();
        }
        catch (InvalidRequestException)
        {
            await Load(purchaseId);
            Scope.ErrorMessage = ChangedMeanwhile;
        }
        finally
        {
            if (!IsReleased)
            {
                Scope.Saving = false;
            }
        }
    }

    private Task OnBack() =>
        new AdminKeys(App) { ListPage = _admin?.Page ?? 1, Customer = _admin?.Customer }.Flip();
}
