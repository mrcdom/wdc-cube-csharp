using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

public sealed class UserCriteria : Criteria<UserCriteria, User>
{
    public UserCriteria()
    {
        UserName = Text("userName");
        Role = Equality(ValueKinds.Enum<Role>(), "role");
    }

    public TextCriterion<UserCriteria> UserName { get; }

    public Criterion<UserCriteria, Role?> Role { get; }
}
