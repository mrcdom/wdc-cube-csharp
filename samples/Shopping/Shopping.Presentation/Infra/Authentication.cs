using Shopping.Domain;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Presentation.Infra;

/// <summary>
/// Opens and closes the session. Locally it checks the user table; through HTTP it answers the server's challenge and
/// keeps the tokens (decision D-19). The presenters do not know which.
/// </summary>
public interface IAuthentication
{
    /// <summary>The subject for these credentials, or <see langword="null"/> when they do not match.</summary>
    Task<Subject?> Login(string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes back a session read from the session store (a reload): the same subject, one with a renewed
    /// <see cref="Subject.Credential"/>, or <see langword="null"/> when it is no longer valid.
    /// </summary>
    Task<Subject?> Resume(Subject subject, CancellationToken cancellationToken = default);

    /// <summary>Ends the session (on the server too, when there is one).</summary>
    Task Logout(Subject subject, CancellationToken cancellationToken = default);
}

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
