using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The users: a customer reads themselves; the password is written, never read.</summary>
public static class UserPolicy
{
    public static DataPolicy<User, UserCriteria> Build() =>
        DataPolicy.For(User.Model, () => new UserCriteria())
            .WriteOnly("password")
            .Criterion("userId", "id")
            .Orderings(c => c.OrderBy switch
            {
                UserOrderBy.NameAToZ => ["name"],
                UserOrderBy.LoginAToZ => ["userName"],
                _ => [],
            })
            .Grant([.. ShoppingPolicies.Super, .. ShoppingPolicies.Admin], g => g
                .Read("userName", "name", "roles")
                .Create("userName", "password", "name", "roles")
                .Update("userName", "password", "name", "roles")
                .Delete())
            .Grant(ShoppingPolicies.Manager, g => g.Read("userName", "name", "roles"))
            .Grant(ShoppingPolicies.Customer, g => g.Rows(OwnUser.Scope).Read("userName", "name", "roles"))
            .Build();
}

/// <summary>The actor's own user.</summary>
internal sealed class OwnUser : IRowScope<User, UserCriteria>
{
    public static readonly OwnUser Scope = new();

    public void Restrict(UserCriteria group, Actor actor) => group.UserId.Eq(ShoppingPolicies.IdOf(actor));

    public ValueTask<bool> Covers(User row, Actor actor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(row.Id == ShoppingPolicies.IdOf(actor));
}
