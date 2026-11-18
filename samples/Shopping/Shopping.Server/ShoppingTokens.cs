using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shopping.Domain;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Server;

/// <summary>Challenges, tokens and the refresh tokens in force (in memory: one server process).</summary>
public sealed class ShoppingTokens(ShoppingAuthOptions options, ShoppingRepositories repositories, TimeProvider time)
{
    public const string RoleClaim = "role";
    public const string UserIdClaim = "sub";
    private const string NameClaim = "name";

    private readonly ConcurrentDictionary<string, (string UserName, DateTimeOffset Expires)> _nonces = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long UserId, DateTimeOffset Expires)> _refresh = new(StringComparer.Ordinal);

    public ShoppingAuthOptions Options => options;

    public static long? UserIdOf(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(UserIdClaim), System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>A single-use nonce, and the salt and iterations of the user's hash (a fake but stable pair when unknown).</summary>
    public async Task<(string Nonce, byte[] Salt, int Iterations)> Challenge(string userName, CancellationToken cancellationToken)
    {
        Sweep();
        var user = await FindUser(userName, cancellationToken);
        if (!PasswordHash.TryRead(user?.Password, out var iterations, out var salt, out _))
        {
            // Unknown user: the same answer every time, so the challenge does not tell who exists.
            salt = HMACSHA256.HashData(options.Key, System.Text.Encoding.UTF8.GetBytes("salt:" + userName))[..16];
            iterations = 100_000;
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        _nonces[nonce] = (userName, time.GetUtcNow() + options.ChallengeLifetime);
        return (nonce, salt, iterations);
    }

    /// <summary>The tokens for an answered challenge, or <see langword="null"/>.</summary>
    public async Task<Tokens?> Login(string userName, string nonce, string digest, CancellationToken cancellationToken)
    {
        if (!_nonces.TryRemove(nonce, out var issued) || issued.UserName != userName || issued.Expires < time.GetUtcNow())
        {
            return null;
        }

        var user = await FindUser(userName, cancellationToken);
        return user is { Id: { } id } && PasswordHash.DigestMatches(user.Password, userName, nonce, digest)
            ? Issue(id, user.Name ?? userName, user.Roles)
            : null;
    }

    /// <summary>New tokens for a refresh token in force (which is spent), or <see langword="null"/>.</summary>
    public async Task<Tokens?> Refresh(string refreshToken, CancellationToken cancellationToken)
    {
        if (!_refresh.TryRemove(refreshToken, out var entry) || entry.Expires < time.GetUtcNow())
        {
            return null;
        }

        var criteria = new UserCriteria().UserId.Eq(entry.UserId)
            .WithProjection(new User { Id = Mark.Int64, Name = Mark.String, Roles = Mark.String });
        var user = (await repositories.Users.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault();
        return user is null ? null : Issue(entry.UserId, user.Name ?? string.Empty, user.Roles);
    }

    public void Revoke(string refreshToken) => _refresh.TryRemove(refreshToken, out _);

    private Tokens Issue(long userId, string name, string? roles)
    {
        var now = time.GetUtcNow();
        var claims = new List<Claim>
        {
            new(UserIdClaim, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(NameClaim, name),
        };
        claims.AddRange((roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => new Claim(RoleClaim, r)));
        var expires = now + options.AccessLifetime;
        var access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Issuer,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256),
        });
        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _refresh[refresh] = (userId, now + options.RefreshLifetime);
        return new Tokens(userId, name, access, refresh, expires, HistoryKeys.Derive(options.HistoryMasterKey, userId));
    }

    private async Task<User?> FindUser(string userName, CancellationToken cancellationToken)
    {
        var criteria = new UserCriteria().UserName.Eq(userName)
            .WithProjection(new User { Id = Mark.Int64, Name = Mark.String, Password = Mark.String, Roles = Mark.String });
        return (await repositories.Users.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault();
    }

    private void Sweep()
    {
        var now = time.GetUtcNow();
        foreach (var (nonce, entry) in _nonces)
        {
            if (entry.Expires < now)
            {
                _nonces.TryRemove(nonce, out _);
            }
        }

        foreach (var (token, entry) in _refresh)
        {
            if (entry.Expires < now)
            {
                _refresh.TryRemove(token, out _);
            }
        }
    }

    public sealed record Tokens(long UserId, string Name, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
        string HistoryKey);
}
