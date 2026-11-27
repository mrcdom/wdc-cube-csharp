using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// Recording an errata is the correction: only reducing or removing items of the purchase, at the price it was bought,
/// never all of them, with a reason. The correction is applied to the items here, in the insert's transaction, through
/// the trusted inside: the item update stays refused to every role of the application. The errata takes its date and
/// its author from the server. Once recorded it does not change and is not deleted, but by maintenance.
/// </summary>
internal sealed class PurchaseAmendmentRules(IPurchaseAmendmentRepository inner, ShoppingRepositories store, TimeProvider time,
    IActorSource actors)
    : RuledRepository<PurchaseAmendment, PurchaseAmendmentCriteria, long>(inner, actors), IPurchaseAmendmentRepository
{
    public override async Task<bool> Insert(PurchaseAmendment bean, PurchaseAmendment? projection = null,
        CancellationToken cancellationToken = default)
    {
        await Apply(bean, cancellationToken);
        return await base.Insert(bean, projection, cancellationToken);
    }

    public override Task<bool> Update(PurchaseAmendment newBean, PurchaseAmendment? oldBean = null, PurchaseAmendment? projection = null,
        CancellationToken cancellationToken = default) =>
        Maintains ? base.Update(newBean, oldBean, projection, cancellationToken) : throw new AccessDeniedException("An errata does not change.");

    public override Task<int> Delete(PurchaseAmendmentCriteria criteria, CancellationToken cancellationToken = default) =>
        Maintains ? base.Delete(criteria, cancellationToken) : throw new AccessDeniedException("An errata is not deleted.");

    private async Task Apply(PurchaseAmendment amendment, CancellationToken cancellationToken)
    {
        if (amendment.Purchase?.Id is not { } purchaseId)
        {
            throw new InvalidRequestException("An errata needs its purchase.");
        }

        var reason = amendment.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new InvalidRequestException("An errata needs its reason.");
        }

        var lines = amendment.Lines ?? [];
        if (lines.Count == 0)
        {
            throw new InvalidRequestException("An errata corrects at least one item.");
        }

        await Lock(purchaseId, cancellationToken);
        var items = (await store.PurchaseItems.Fetch(new PurchaseItemCriteria()
                .PurchaseId.Eq(purchaseId)
                .WithProjection(new PurchaseItem
                {
                    Id = Mark.Int64,
                    Amount = Mark.Int32,
                    Price = Mark.Decimal,
                    Product = new Product { Id = Mark.Int64 },
                }), cancellationToken: cancellationToken))
            .ToDictionary(i => i.Id!.Value);

        var corrected = new HashSet<long>();
        foreach (var line in lines)
        {
            if (line.ItemId is not { } itemId || !items.TryGetValue(itemId, out var item))
            {
                throw new InvalidRequestException($"The item {line.ItemId} is not of the purchase {purchaseId}.");
            }

            if (!corrected.Add(itemId))
            {
                throw new InvalidRequestException($"The item {itemId} is corrected twice.");
            }

            if (line.AmountBefore is { } before && before != item.Amount)
            {
                throw new InvalidRequestException(
                    $"The item {itemId} has {item.Amount}, not {before}: it was corrected meanwhile.");
            }

            if (line.AmountAfter is not { } after || after < 0 || after >= item.Amount)
            {
                throw new InvalidRequestException(
                    $"The item {itemId} can only go down from {item.Amount} (0 removes it), not to {line.AmountAfter}.");
            }
        }

        if (items.Values.All(i => lines.Any(l => l.ItemId == i.Id && l.AmountAfter == 0)))
        {
            throw new InvalidRequestException("An errata does not remove every item of the purchase.");
        }

        foreach (var line in lines)
        {
            var item = items[line.ItemId!.Value];
            if (line.AmountAfter == 0)
            {
                await store.PurchaseItems.Delete(new PurchaseItemCriteria().PurchaseItemId.Eq(item.Id), cancellationToken);
            }
            else
            {
                await store.PurchaseItems.Update(new PurchaseItem { Id = item.Id, Amount = line.AmountAfter }, null,
                    new PurchaseItem { Amount = Mark.Int32 }, cancellationToken);
            }

            // What the item was, from the item, whatever was sent.
            line.Id = null;
            line.Amendment = null;
            line.AmountBefore = item.Amount;
            line.Price = item.Price;
            line.Product = new Product { Id = item.Product?.Id };
        }

        amendment.Id = null;
        amendment.Reason = reason;
        amendment.Date = time.GetUtcNow();
        amendment.Purchase = new Purchase { Id = purchaseId };
        amendment.Author = new User { Id = ShoppingPolicies.IdOf(Actor) };
    }

    /// <summary>
    /// The purchase, if it was made, locked until the transaction ends: two errata of the same purchase are applied one
    /// after the other, each on what the other left.
    /// </summary>
    private async Task Lock(long purchaseId, CancellationToken cancellationToken)
    {
        var purchase = await store.Purchases.FetchById(purchaseId, new Purchase { BuyDate = Mark.Instant }, cancellationToken)
                       ?? throw new KeyNotFoundException($"No purchase {purchaseId}.");
        if (purchase.BuyDate is not { } date)
        {
            throw new InvalidRequestException("Only a purchase already made takes an errata.");
        }

        // Writing the row (to what it holds) takes its lock, in every database.
        await store.Purchases.Update(new Purchase { Id = purchaseId, BuyDate = date }, null,
            new Purchase { BuyDate = Mark.Instant }, cancellationToken);
    }
}
