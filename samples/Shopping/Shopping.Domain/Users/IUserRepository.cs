using Wdc.Domain;

namespace Shopping.Domain.Users;

public interface IUserRepository : IRepository<User, UserCriteria, long>;
