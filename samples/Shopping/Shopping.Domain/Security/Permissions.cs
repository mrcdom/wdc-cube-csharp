using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// What the screens offer each role (<c>entity:operation</c>, as <see cref="Has"/> asks), who acts, and who maintains
/// the solution. Who reaches what data is <see cref="ShoppingPolicies"/>; only <see cref="Role.Super"/>, the maintenance
/// role, has the business rules marked as relaxable relaxed (<see cref="Maintains"/>).
/// </summary>
public static class Permissions
{
    private static readonly Dictionary<Role, string[]> s_grants = new()
    {
        [Role.Super] = ["*"],
        [Role.Admin] = ["*"],
        [Role.Manager] = ["user:read", "product:read", "product:write", "product:delete", "purchase:read", "purchase-item:read",
            "purchase-amendment:read", "purchase-amendment:write"],
        [Role.Customer] = ["user:read", "product:read", "purchase:read", "purchase:write", "purchase-item:read", "purchase-item:write",
            "purchase-item:delete", "purchase-amendment:read"],
    };

    /// <summary>Who acts: the user's id and roles (comma-separated <see cref="Role"/> names, as the user record keeps them).</summary>
    public static Actor ActorOf(long userId, string? roles) =>
        new(userId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static bool Has(Actor actor, string permission)
    {
        ArgumentNullException.ThrowIfNull(actor);
        foreach (var name in actor.Roles)
        {
            if (Enum.TryParse<Role>(name, out var role) && s_grants.TryGetValue(role, out var grants)
                && (grants.Contains("*") || grants.Contains(permission)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the actor maintains the solution: the rules marked as relaxable are relaxed for them.</summary>
    public static bool Maintains(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.HasRole(nameof(Role.Super));
    }
}
