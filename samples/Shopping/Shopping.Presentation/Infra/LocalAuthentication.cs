using Shopping.Domain;
using Shopping.Domain.Security;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Presentation.Infra;

/// <summary>
/// Checks credentials against the user table, in this process, and says who is acting to the session's protected
/// repositories.
/// </summary>
/// <param name="store">Where the users are: the trusted inside (nobody is acting before the login).</param>
/// <param name="actors">The actor of the session's protected repositories: set at login and resume, cleared at logout.</param>
/// <param name="historyKeyOf">The address key of a user (the host derives it from its master key); none: plain addresses.</param>
/// <param name="throttle">Slows down password guessing (a server host); none: every attempt is checked.</param>
public sealed class LocalAuthentication(ShoppingRepositories store, ActorSource actors, Func<long, string>? historyKeyOf = null,
    LoginThrottle? throttle = null)
    : IAuthentication
{
    private static readonly User s_projection = new() { Id = Mark.Int64, Name = Mark.String, Password = Mark.String, Roles = Mark.String };

    public async Task<Subject?> Login(string userName, string password, CancellationToken cancellationToken = default)
    {
        actors.Current = null;
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        userName = userName.Trim();
        if (throttle is not null)
        {
            await throttle.Check(userName, cancellationToken);
        }

        var criteria = new UserCriteria().UserName.Eq(userName).WithProjection(s_projection);
        var user = (await store.Users.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault();
        var matches = user is { Id: not null } && PasswordHash.Verify(password, user.Password);
        if (throttle is not null)
        {
            await (matches ? throttle.Succeeded(userName, cancellationToken) : throttle.Failed(userName, cancellationToken));
        }

        return matches ? Enter(user!, userName) : null;
    }

    /// <summary>Reads the user again: one deleted since, or whose roles changed, does not go on as before.</summary>
    public async Task<Subject?> Resume(Subject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        actors.Current = null;
        var criteria = new UserCriteria().UserId.Eq(subject.Id).WithProjection(s_projection);
        var user = (await store.Users.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault();
        return user is null ? null : Enter(user, subject.NickName);
    }

    public Task Logout(Subject subject, CancellationToken cancellationToken = default)
    {
        actors.Current = null;
        return Task.CompletedTask;
    }

    private Subject Enter(User user, string fallbackName)
    {
        var id = user.Id!.Value;
        actors.Current = Permissions.ActorOf(id, user.Roles);
        return new Subject(id, user.Name ?? fallbackName, HistoryKey: historyKeyOf?.Invoke(id), Roles: user.Roles);
    }
}
