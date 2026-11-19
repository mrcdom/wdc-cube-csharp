using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The users: each reaches themselves (unless their reach is everything); the password hash never goes out.</summary>
public static class UserPolicy
{
    public static RepositoryPolicy<User, UserCriteria, long> Create() => new(User.Model, () => new UserCriteria())
    {
        Authorize = Permissions.Allow("user"),
        Scope = (c, criteria) => Permissions.Reach(c, id => criteria.UserId.Restrict(id)),
        RestrictToId = (criteria, id) => criteria.UserId.Restrict(id),
        SanitizeProjection = (_, p) => p.Password = null,
        SanitizeOutput = (_, u) => u.Password = null,
        BeforeInsert = (_, u, _) =>
        {
            u.Password = u.Password is null ? null : PasswordHash.Hash(u.Password);
            return ValueTask.CompletedTask;
        },
        BeforeUpdate = (_, u, written, _) =>
        {
            if (written.Password is not null && u.Password is not null)
            {
                u.Password = PasswordHash.Hash(u.Password);
            }

            return ValueTask.CompletedTask;
        },
    };

    internal static void DropPassword(User? user)
    {
        if (user is not null)
        {
            user.Password = null;
        }
    }
}
