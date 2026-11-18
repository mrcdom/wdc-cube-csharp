using Shopping.Domain;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Presentation.Infra;

/// <summary>Checks credentials against the user table, in this process.</summary>
/// <param name="repositories">Where the users are.</param>
/// <param name="historyKeyOf">The address key of a user (the host derives it from its master key); none: plain addresses.</param>
public sealed class LocalAuthentication(ShoppingRepositories repositories, Func<long, string>? historyKeyOf = null) : IAuthentication
{
    public async Task<Subject?> Login(string userName, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var criteria = new UserCriteria()
            .UserName.Eq(userName.Trim())
            .WithProjection(new User { Id = Mark.Int64, Name = Mark.String, Password = Mark.String });
        var user = (await repositories.Users.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault();

        return user is { Id: { } id } && PasswordHash.Verify(password, user.Password)
            ? new Subject(id, user.Name ?? userName, HistoryKey: historyKeyOf?.Invoke(id))
            : null;
    }

    public Task<Subject?> Resume(Subject subject, CancellationToken cancellationToken = default) => Task.FromResult<Subject?>(subject);

    public Task Logout(Subject subject, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
