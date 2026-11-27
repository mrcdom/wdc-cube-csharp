using Shopping.Domain.Users;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>Users: their table and their criteria. The password is a column, never a criterion.</summary>
public sealed class UserRepository(Database database)
    : SqlRepository<User, UserCriteria, long>(database, Mapping), IUserRepository
{
    /// <summary>How a user is stored; every relation that reaches a user uses it (and its conditions).</summary>
    public static readonly EntityMapping<User> Mapping = EntityMapping.For(User.Model, "user_account", "us")
        .Key("id", generated: true)
        .Column("userName", "user_name")
        .Column("password")
        .Column("name")
        .Column("roles")
        .Conditions<UserCriteria>(Conditions)
        .Ordering<UserOrderBy>(o => o switch
        {
            UserOrderBy.NewestFirst => [SortSpec.Desc("id")],
            UserOrderBy.NameAToZ => [SortSpec.Asc("name")],
            UserOrderBy.LoginAToZ => [SortSpec.Asc("user_name")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    /// <summary>The user's criteria on its table.</summary>
    private static ISqlFragment? Conditions(UserCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.UserId),
        t.Translate("user_name", criteria.UserName),
        t.Translate("name", criteria.Name),
        t.Translate("roles", criteria.Roles),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);
}
