using Shopping.Domain;
using Shopping.Domain.Users;
using Shopping.Presentation.Infra;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Login;

/// <summary>Checks credentials against the user table.</summary>
public sealed class LoginService(ShoppingRepositories repositories)
{
    /// <summary>The subject for these credentials, or <see langword="null"/> when they do not match.</summary>
    public async Task<Subject?> Authenticate(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var criteria = new UserCriteria()
            .UserName.Eq(userName.Trim())
            .WithProjection(new User { Id = Mark.Int64, Name = Mark.String, Password = Mark.String });
        var user = (await repositories.Users.Fetch(criteria, limit: 1)).FirstOrDefault();

        return user is { Id: { } id } && PasswordHash.Verify(password, user.Password)
            ? new Subject(id, user.Name ?? userName)
            : null;
    }
}
