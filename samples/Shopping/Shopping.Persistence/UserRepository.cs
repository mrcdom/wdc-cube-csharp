using Shopping.Domain.Users;
using Wdc.Persistence;

namespace Shopping.Persistence;

public sealed class UserRepository(Database database)
    : SqlRepository<User, UserCriteria, long>(database, ShoppingMappings.User), IUserRepository;
