using Wdc.Domain;

namespace Shopping.Domain.Users;

public sealed class UserCriteria : Criteria<UserCriteria, User, UserOrderBy>
{
    public UserCriteria()
    {
        UserId = Comparable(ValueKinds.Int64, "userId");
        UserName = Text("userName");
        Name = Text("name");
        Roles = Text("roles");
    }

    public ComparableCriterion<UserCriteria, long?> UserId { get; }

    /// <summary>The login. The password is deliberately not a criterion.</summary>
    public TextCriterion<UserCriteria> UserName { get; }

    public TextCriterion<UserCriteria> Name { get; }

    public TextCriterion<UserCriteria> Roles { get; }
}
