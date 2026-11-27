using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>Users of the test domain.</summary>
public sealed class UserRepository(Database database) : SqlRepository<User, UserCriteria, long>(database, Mapping)
{
    public static readonly EntityMapping<User> Mapping = EntityMapping.For(User.Model, "user_account", "u")
        .Key("id", generated: true)
        .Column("userName", "user_name")
        .Column("password")
        .Column("name")
        .Column("role")
        .Conditions<UserCriteria>(Conditions)
        .Build();

    private static ISqlFragment? Conditions(UserCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("user_name", criteria.UserName),
        t.Translate("role", criteria.Role),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);
}
