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

    /// <summary>
    /// Reads a stored hash: the iterations, the salt and the derived bytes. A login challenge hands the first two to
    /// the client, which derives the same bytes from the password without sending it (decision D-19).
    /// </summary>
    public static bool TryRead(string? stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        var parts = stored?.Split('$');
        if (parts is not [Scheme, var iterationsText, var saltText, var hashText]
            || !int.TryParse(iterationsText, System.Globalization.CultureInfo.InvariantCulture, out iterations))
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(saltText);
            hash = Convert.FromBase64String(hashText);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The bytes derived from <paramref name="password"/> with this salt and iteration count.</summary>
    public static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);

    /// <summary>The answer to a login challenge: <c>HMAC-SHA256(derived, userName + nonce)</c>, in Base64.</summary>
    public static string Digest(byte[] derived, string userName, string nonce)
    {
        ArgumentNullException.ThrowIfNull(derived);
        return Convert.ToBase64String(HMACSHA256.HashData(derived, System.Text.Encoding.UTF8.GetBytes(userName + nonce)));
    }

    /// <summary>Whether a challenge answer matches the stored hash, in constant time.</summary>
    public static bool DigestMatches(string? stored, string userName, string nonce, string digest)
    {
        if (!TryRead(stored, out _, out _, out var hash))
        {
            return false;
        }

        byte[] given;
        try
        {
            given = Convert.FromBase64String(digest);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Convert.FromBase64String(Digest(hash, userName, nonce));
        return CryptographicOperations.FixedTimeEquals(given, expected);
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
