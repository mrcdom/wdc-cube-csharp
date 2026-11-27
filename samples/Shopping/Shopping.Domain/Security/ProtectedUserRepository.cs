using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The user repository as an actor sees it: through <see cref="UserPolicy"/>.</summary>
public sealed class ProtectedUserRepository(IUserRepository inner, DataPolicy<User, UserCriteria> policy, Protection protection)
    : ProtectedRepository<User, UserCriteria, long>(inner, policy, (c, id) => c.UserId.Restrict(id), protection), IUserRepository;
