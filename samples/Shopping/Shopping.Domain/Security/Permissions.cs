using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// What a role may do: <c>entity:operation</c> (read, write, delete) and <c>data:all</c> (no per-user reach). The
/// business rules hold for every role; only <see cref="Role.Super"/>, the maintenance role, has some of them relaxed
/// (<see cref="Maintains"/>).
/// </summary>
public static class Permissions
{
    public const string AllData = "data:all";

    private static readonly Dictionary<Role, string[]> s_grants = new()
    {
        [Role.Super] = ["*", AllData],
        [Role.Admin] = ["*", AllData],
        [Role.Manager] = ["user:read", "product:read", "product:write", "product:delete", "purchase:read", "purchase-item:read",
            "purchase-amendment:read", "purchase-amendment:write", AllData],
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

    /// <summary>Throws unless the actor may do <paramref name="operation"/> on <paramref name="entity"/>.</summary>
    public static void Require(Actor actor, string entity, RepositoryOperation operation)
    {
        var verb = operation switch
        {
            RepositoryOperation.Insert or RepositoryOperation.Update => "write",
            RepositoryOperation.Delete => "delete",
            _ => "read",
        };
        if (!Has(actor, $"{entity}:{verb}"))
        {
            throw new AccessDeniedException($"Not allowed: {entity}:{verb}.");
        }
    }

    /// <summary>The actor's id, when their reach is only their own data; <see langword="null"/> when it is everything.</summary>
    public static long? ReachOf(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Has(actor, AllData) ? null : long.Parse(actor.Id, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the actor maintains the solution: the rules marked as relaxable are relaxed for them.</summary>
    public static bool Maintains(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.HasRole(nameof(Role.Super));
    }

    /// <summary>The authorization of an entity's policy.</summary>
    internal static Action<PolicyContext, RepositoryOperation> Allow(string entity) =>
        (context, operation) => Require(context.Actor, entity, operation);

    /// <summary>Restricts to the actor's own data, unless their reach is everything.</summary>
    internal static void Reach(PolicyContext context, Action<long> restrict)
    {
        if (ReachOf(context.Actor) is { } owner)
        {
            restrict(owner);
        }
    }
}
