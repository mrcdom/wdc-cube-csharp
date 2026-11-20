using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The errata of the purchases. Inserting one is the correction: only reducing or removing items of
/// the purchase, at the price it was bought, never all of them, with a reason. The correction is applied to the items
/// here, in the insert's transaction, through the trusted inside — the generic item update stays refused to every
/// role of the application. Once inserted an errata does not change and is not deleted, except by maintenance.
/// Whoever corrected it is seen only by roles that reach all the data.
/// </summary>
public static class PurchaseAmendmentPolicy
{
    /// <param name="store">The repositories the rules read and the correction writes (the trusted inside).</param>
    /// <param name="time">The clock of the errata's date.</param>
    public static RepositoryPolicy<PurchaseAmendment, PurchaseAmendmentCriteria, long> Create(ShoppingRepositories store,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        return new RepositoryPolicy<PurchaseAmendment, PurchaseAmendmentCriteria, long>(PurchaseAmendment.Model,
            () => new PurchaseAmendmentCriteria())
        {
            Authorize = Permissions.Allow("purchase-amendment"),
            Scope = (c, criteria) => Permissions.Reach(c, id => criteria.UserId.Restrict(id)),
            RestrictToId = (criteria, id) => criteria.PurchaseAmendmentId.Restrict(id),
            SanitizeProjection = Sanitize,
            SanitizeOutput = Sanitize,
            BeforeInsert = (c, amendment, ct) => new ValueTask(Apply(store, time, c, amendment, ct)),
            BeforeUpdate = (c, _, _, _) => Permissions.Maintains(c.Actor)
                ? ValueTask.CompletedTask
                : throw new AccessDeniedException("An errata does not change."),
            BeforeDelete = (c, _, _) => Permissions.Maintains(c.Actor)
                ? ValueTask.CompletedTask
                : throw new AccessDeniedException("An errata is not deleted."),
        };
    }

    /// <summary>
    /// What an errata shows the actor (as a projection or as data): the author's password never; the author only to
    /// whoever reaches all the data.
    /// </summary>
    internal static void Sanitize(PolicyContext context, PurchaseAmendment? amendment)
    {
        if (amendment is null)
        {
            return;
        }

        if (Permissions.ReachOf(context.Actor) is not null)
        {
            amendment.Author = null;
        }

        UserPolicy.DropPassword(amendment.Author);
        UserPolicy.DropPassword(amendment.Purchase?.User);
    }

    /// <summary>The errata of a purchase, as a projection (its shape) or as data (each one).</summary>
    internal static void Sanitize(PolicyContext context, List<PurchaseAmendment>? amendments)
    {
        if (amendments is ProjectionList<PurchaseAmendment> projection)
        {
            Sanitize(context, projection.Shape);
            return;
        }

        foreach (var amendment in amendments ?? [])
        {
            Sanitize(context, amendment);
        }
    }

    private static async Task Apply(ShoppingRepositories store, TimeProvider time, PolicyContext context,
        PurchaseAmendment amendment, CancellationToken cancellationToken)
    {
        if (context.InCascade)
        {
            throw new AccessDeniedException("An errata is inserted by itself, not with its purchase.");
        }

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

        await Lock(store, context, purchaseId, cancellationToken);
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
        amendment.Author = new User { Id = long.Parse(context.Actor.Id, System.Globalization.CultureInfo.InvariantCulture) };
    }

    /// <summary>
    /// The purchase, if the actor reaches it and it was made, locked until the transaction ends: two errata of the
    /// same purchase are applied one after the other, each on what the other left.
    /// </summary>
    private static async Task Lock(ShoppingRepositories store, PolicyContext context, long purchaseId,
        CancellationToken cancellationToken)
    {
        var criteria = new PurchaseCriteria().PurchaseId.Restrict(purchaseId).WithProjection(new Purchase { BuyDate = Mark.Instant });
        Permissions.Reach(context, owner => criteria.UserId.Restrict(owner));
        var purchase = (await store.Purchases.Fetch(criteria, 0, 1, cancellationToken)).FirstOrDefault()
                       ?? throw new KeyNotFoundException($"No purchase {purchaseId} within reach.");
        if (purchase.BuyDate is not { } date)
        {
            throw new InvalidRequestException("Only a purchase already made takes an errata.");
        }

        // Writing the row (to what it holds) takes its lock, in every database.
        await store.Purchases.Update(new Purchase { Id = purchaseId, BuyDate = date }, null,
            new Purchase { BuyDate = Mark.Instant }, cancellationToken);
    }
}
