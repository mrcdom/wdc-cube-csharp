using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The lines of the errata (decision D-25): they enter only with their errata, which fills them in, and then do not
/// change nor leave, except by maintenance. Read by whoever reads the errata.
/// </summary>
public static class PurchaseAmendmentLinePolicy
{
    public static RepositoryPolicy<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria, long> Create() =>
        new(PurchaseAmendmentLine.Model, () => new PurchaseAmendmentLineCriteria())
        {
            Authorize = Permissions.Allow("purchase-amendment"),
            Scope = (c, criteria) => Permissions.Reach(c, id => criteria.UserId.Restrict(id)),
            RestrictToId = (criteria, id) => criteria.PurchaseAmendmentLineId.Restrict(id),
            SanitizeProjection = (c, l) => PurchaseAmendmentPolicy.Sanitize(c, l.Amendment),
            SanitizeOutput = (c, l) => PurchaseAmendmentPolicy.Sanitize(c, l.Amendment),
            BeforeInsert = (c, _, _) => c.InCascade || Permissions.Maintains(c.Actor)
                ? ValueTask.CompletedTask
                : throw new AccessDeniedException("A line enters only with its errata."),
            BeforeUpdate = (c, _, _, _) => Permissions.Maintains(c.Actor)
                ? ValueTask.CompletedTask
                : throw new AccessDeniedException("An errata does not change."),
            BeforeDelete = (c, _, _) => Permissions.Maintains(c.Actor)
                ? ValueTask.CompletedTask
                : throw new AccessDeniedException("An errata is not deleted."),
        };
}
