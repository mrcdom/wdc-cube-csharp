using System.Security.Claims;
using Shopping.Domain.Users;
using Wdc.Domain;
using Wdc.Rest.Server;

namespace Shopping.Server;

/// <summary>What a role may do: <c>entity:operation</c> (read, write, delete) and <c>data:all</c> (no per-user reach).</summary>
public static class Permissions
{
    public const string AllData = "data:all";

    private static readonly Dictionary<Role, string[]> s_grants = new()
    {
        [Role.Admin] = ["*", AllData],
        [Role.Manager] = ["user:read", "product:read", "product:write", "product:delete", "purchase:read", "purchase-item:read", AllData],
        [Role.Customer] = ["user:read", "product:read", "purchase:read", "purchase:write", "purchase-item:read", "purchase-item:write"],
    };

    public static bool Has(ClaimsPrincipal user, string permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        foreach (var claim in user.FindAll(ShoppingTokens.RoleClaim))
        {
            if (Enum.TryParse<Role>(claim.Value, out var role) && s_grants.TryGetValue(role, out var grants)
                && (grants.Contains("*") || grants.Contains(permission)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Throws unless the caller may do <paramref name="operation"/> on <paramref name="entity"/>.</summary>
    public static void Require(ClaimsPrincipal user, string entity, RpcOperation operation)
    {
        var verb = operation switch
        {
            RpcOperation.Insert or RpcOperation.Update => "write",
            RpcOperation.Delete => "delete",
            _ => "read",
        };
        if (!Has(user, $"{entity}:{verb}"))
        {
            throw new AccessDeniedException($"Not allowed: {entity}:{verb}.");
        }
    }

    /// <summary>The caller's id, when their reach is only their own data; <see langword="null"/> when it is everything.</summary>
    public static long? ReachOf(ClaimsPrincipal user) =>
        Has(user, AllData) ? null : ShoppingTokens.UserIdOf(user) ?? throw new AuthenticationException("Authentication required.");
}
