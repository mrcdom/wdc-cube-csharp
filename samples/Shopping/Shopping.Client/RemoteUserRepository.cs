using Shopping.Domain.Users;
using Wdc.Rest.Client;

namespace Shopping.Client;

public sealed class RemoteUserRepository(RestClient client)
    : RemoteRepository<User, UserCriteria, long>(client, "/api/repo/user", User.Model, () => new UserCriteria()), IUserRepository;
