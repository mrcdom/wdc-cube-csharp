using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home.Admin;
using Wdc.Cube;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Admin.Amend;

/// <summary>
/// Corrects a purchase already made: a smaller quantity per item (0 removes it), never all of them,
/// with a reason. The repository checks it all again; what it refuses here means the purchase changed meanwhile.
/// </summary>
public sealed class AmendPresenter(RootPresenter app) : CubePresenter<RootPresenter, AmendScope>(app, new AmendScope())
{
    public const string NothingToCorrect = "Lower or remove at least one item.";
    public const string ReasonRequired = "Enter the reason for the correction.";
    public const string EveryItemRemoved = "The correction cannot remove every item.";
    public const string OnlyDown = "The quantity can only go down (0 removes the item).";
    public const string ChangedMeanwhile = "The purchase changed while you were correcting it. Check it and try again.";
    public const string Saved = "Correction recorded.";

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
            Scope.OnChangeReason = Action<string>(reason =>
            {
                Scope.Reason = reason;
                Refresh();
            });
            Scope.OnConfirm = Action(Confirm);
            Scope.OnBack = Action(Back);
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
        var criteria = new PurchaseCriteria()
            .PurchaseId.Eq(purchaseId)
            .WithProjection(new Purchase
            {
                Id = Mark.Int64,
                BuyDate = Mark.Instant,
                User = new User { Name = Mark.String },
                Items = Mark.List(
                    new PurchaseItem { Id = Mark.Int64, Price = Mark.Decimal, Amount = Mark.Int32, Product = new Product { Name = Mark.String } },
                    new PurchaseItemCriteria().WithOrderBy(PurchaseItemOrderBy.OldestFirst)),
                Amendments = Mark.List(
                    new PurchaseAmendment
                    {
                        Date = Mark.Instant,
                        Reason = Mark.String,
                        Author = new User { Name = Mark.String },
                        Lines = Mark.List(
                            new PurchaseAmendmentLine { AmountBefore = Mark.Int32, AmountAfter = Mark.Int32, Product = new Product { Name = Mark.String } },
                            new PurchaseAmendmentLineCriteria().WithOrderBy(PurchaseAmendmentLineOrderBy.OldestFirst)),
                    },
                    new PurchaseAmendmentCriteria().WithOrderBy(PurchaseAmendmentOrderBy.OldestFirst)),
            });
        var purchase = (await App.Repositories.Purchases.Fetch(criteria, limit: 1)).FirstOrDefault()
                       ?? throw new PurchaseNotFoundException(purchaseId);

        _purchaseId = purchaseId;
        Scope.TitleLabel = $"Purchase #{purchaseId}";
        Scope.CustomerLabel = purchase.User?.Name ?? string.Empty;
        Scope.DateLabel = purchase.BuyDate is { } date ? Formats.Date(date) : string.Empty;
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
        line.OnChangeNewQuantity = Action<int>(quantity =>
        {
            if (quantity < 0 || quantity > line.Quantity)
            {
                Scope.ErrorMessage = OnlyDown;
                return;
            }

            line.NewQuantity = quantity;
            Scope.ErrorMessage = null;
            Refresh();
        });
        return line;
    }

    private decimal Total(Func<AmendLineScope, int> quantity) =>
        Scope.Lines.Sum(l => _prices.GetValueOrDefault(l.ItemId) * quantity(l));

    private static AmendmentEntry ToEntry(PurchaseAmendment a) => new(
        a.Date is { } date ? Formats.Date(date) : string.Empty,
        a.Author?.Name ?? string.Empty,
        a.Reason ?? string.Empty,
        [.. (a.Lines ?? []).Select(l => $"{l.Product?.Name}: {l.AmountBefore} → {l.AmountAfter}")]);

    /// <summary>What follows from the lines and the reason: the new total and whether to offer the confirmation.</summary>
    private void Refresh()
    {
        Scope.NewTotalLabel = Formats.Money(Total(l => l.NewQuantity));
        Scope.CanConfirm = Problem() is null;
    }

    /// <summary>Why the correction cannot be confirmed as it is, or <see langword="null"/>.</summary>
    private string? Problem()
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

    private async Task Confirm()
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
            await App.Repositories.Amendments.Insert(errata);
            await Load(purchaseId);
            Scope.SuccessMessage = Saved;
            _ = _admin?.Reload();
        }
        catch (InvalidRequestException)
        {
            await Load(purchaseId);
            Scope.ErrorMessage = ChangedMeanwhile;
        }
        catch (Exception caught) when (caught is not OperationCanceledException)
        {
            App.Unexpected("Recording the correction", caught);
        }
        finally
        {
            if (!IsReleased)
            {
                Scope.Saving = false;
            }
        }
    }

    private Task Back() =>
        new AdminKeys(App) { ListPage = _admin?.Page ?? 1, Customer = _admin?.Customer }.Flip();
}
