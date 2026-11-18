using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>A criteria with a field the user mapping does not know.</summary>
public sealed class UserCriteriaWithUnmappedField : Criteria<UserCriteriaWithUnmappedField, User>
{
    public UserCriteriaWithUnmappedField() => Nickname = Text("nickname");

    public TextCriterion<UserCriteriaWithUnmappedField> Nickname { get; }
}
