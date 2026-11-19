using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The user repository as an actor sees it: through <see cref="UserPolicy"/>.</summary>
public sealed class ProtectedUserRepository(IUserRepository inner, Protection protection)
    : ProtectedRepository<User, UserCriteria, long>(inner, UserPolicy.Create(), protection), IUserRepository;
