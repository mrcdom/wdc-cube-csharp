using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The password is stored as its hash, whoever writes it.</summary>
internal sealed class UserRules(IUserRepository inner, IActorSource actors)
    : RuledRepository<User, UserCriteria, long>(inner, actors), IUserRepository
{
    public override Task<bool> Insert(User bean, User? projection = null, CancellationToken cancellationToken = default)
    {
        bean.Password = bean.Password is null ? null : PasswordHash.Hash(bean.Password);
        return base.Insert(bean, projection, cancellationToken);
    }

    public override Task<bool> Update(User newBean, User? oldBean = null, User? projection = null, CancellationToken cancellationToken = default)
    {
        if (Written(User.Model, newBean, oldBean, projection).Password is not null && newBean.Password is not null)
        {
            newBean.Password = PasswordHash.Hash(newBean.Password);
        }

        return base.Update(newBean, oldBean, projection, cancellationToken);
    }
}
