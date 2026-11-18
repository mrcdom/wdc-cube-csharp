using System.Security.Cryptography;
using Wdc.Domain;

namespace Shopping.Domain.Users;

public sealed class User : IKeyedEntity
{
    public static readonly EntityModel<User> Model = EntityModel.For(() => new User())
        .Key(ValueKinds.Int64, "id", u => u.Id, (u, v) => u.Id = v)
        .Field(ValueKinds.String, "userName", u => u.UserName, (u, v) => u.UserName = v)
        .Field(ValueKinds.String, "password", u => u.Password, (u, v) => u.Password = v, inDefaultProjection: false)
        .Field(ValueKinds.String, "name", u => u.Name, (u, v) => u.Name = v)
        .Field(ValueKinds.String, "roles", u => u.Roles, (u, v) => u.Roles = v)
        .Build();

    public long? Id { get; set; }

    /// <summary>The login.</summary>
    public string? UserName { get; set; }

    /// <summary>The password hash (<see cref="PasswordHash"/>). Never in the default projection.</summary>
    public string? Password { get; set; }

    /// <summary>The display name.</summary>
    public string? Name { get; set; }

    /// <summary>Comma-separated <see cref="Role"/> names.</summary>
    public string? Roles { get; set; }

    object? IKeyedEntity.Key => Id;
}

public enum Role
{
    Admin,
    Customer,
    Manager,
}

public enum UserOrderBy
{
    OldestFirst,
    NewestFirst,
    NameAToZ,
    LoginAToZ,
}

public sealed class UserCriteria : Criteria<UserCriteria, User, UserOrderBy>
{
    public UserCriteria()
    {
        UserId = Comparable(ValueKinds.Int64, "userId");
        UserName = Text("userName");
        Name = Text("name");
        Roles = Text("roles");
    }

    public ComparableCriterion<UserCriteria, long?> UserId { get; }

    /// <summary>The login. The password is deliberately not a criterion.</summary>
    public TextCriterion<UserCriteria> UserName { get; }

    public TextCriterion<UserCriteria> Name { get; }

    public TextCriterion<UserCriteria> Roles { get; }
}

public interface IUserRepository : IRepository<User, UserCriteria, long>;

/// <summary>
/// Password hashing: PBKDF2-SHA256 with a random salt, stored as <c>pbkdf2-sha256$iterations$salt$hash</c>
/// (decision D-15; the Kotlin demo used an unsalted MD5).
/// </summary>
public static class PasswordHash
{
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Scheme = "pbkdf2-sha256";

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Whether <paramref name="password"/> matches <paramref name="stored"/>, in constant time.</summary>
    public static bool Verify(string password, string? stored)
    {
        ArgumentNullException.ThrowIfNull(password);
        var parts = stored?.Split('$');
        if (parts is not [Scheme, var iterationsText, var saltText, var hashText]
            || !int.TryParse(iterationsText, System.Globalization.CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(hashText);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(saltText), iterations,
                HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
