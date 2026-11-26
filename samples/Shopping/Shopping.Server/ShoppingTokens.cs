using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shopping.Domain.Security;
using Shopping.Domain.Users;
using Shopping.Persistence;
using Wdc.Domain;

namespace Shopping.Server;

/// <summary>
/// Challenges, tokens and the refresh tokens in force. The challenges and refresh tokens live in the database (of a
/// refresh token, only its hash), so any instance answers them, and each is spent once.
/// </summary>
/// <remarks>Reads the users through the store itself: nobody is acting yet.</remarks>
public sealed class ShoppingTokens(ShoppingAuthOptions options, ShoppingStore store, TimeProvider time)
{
    public const string RoleClaim = "role";
    public const string UserIdClaim = "sub";
    private const string NameClaim = "name";
    private static readonly TimeSpan s_sweepEvery = TimeSpan.FromMinutes(1);

    private readonly LoginThrottle _throttle = new(store.Security, time);
    private long _nextSweep;

    public ShoppingAuthOptions Options => options;

    /// <summary>Who acts for an authenticated request: the token's user and roles; nobody without them.</summary>
    public static Actor? ActorOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true
               && long.TryParse(user.FindFirstValue(UserIdClaim), System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? Permissions.ActorOf(id, string.Join(',', user.FindAll(RoleClaim).Select(c => c.Value)))
            : null;
    }

    /// <summary>A single-use nonce, and the salt and iterations of the user's hash (a fake but stable pair when unknown).</summary>
    public async Task<(string Nonce, byte[] Salt, int Iterations)> Challenge(string userName, CancellationToken cancellationToken)
    {
        await Sweep(cancellationToken);
        var user = await FindUser(userName, cancellationToken);
        if (!PasswordHash.TryRead(user?.Password, out var iterations, out var salt, out _))
        {
            // Unknown user: the same answer every time, so the challenge does not tell who exists.
            salt = HMACSHA256.HashData(options.Key, System.Text.Encoding.UTF8.GetBytes("salt:" + userName))[..16];
            iterations = 100_000;
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        await store.Security.SaveNonce(nonce, userName, time.GetUtcNow() + options.ChallengeLifetime, cancellationToken);
        return (nonce, salt, iterations);
    }

    /// <summary>The tokens for an answered challenge, or <see langword="null"/>.</summary>
    /// <exception cref="Wdc.Domain.TooManyRequestsException">Too many failures in a row for this user name.</exception>
    public async Task<Tokens?> Login(string userName, string nonce, string digest, CancellationToken cancellationToken)
    {
        await _throttle.Check(userName, cancellationToken);
        if (await store.Security.SpendNonce(nonce, userName, time.GetUtcNow(), cancellationToken)
            && await FindUser(userName, cancellationToken) is { Id: { } id } user
            && PasswordHash.DigestMatches(user.Password, userName, nonce, digest))
        {
            await _throttle.Succeeded(userName, cancellationToken);
            return await Issue(id, user.Name ?? userName, user.Roles, cancellationToken);
        }

        await _throttle.Failed(userName, cancellationToken);
        return null;
    }

    /// <summary>New tokens for a refresh token in force (which is spent), or <see langword="null"/>.</summary>
    public async Task<Tokens?> Refresh(string refreshToken, CancellationToken cancellationToken)
    {
        if (await store.Security.SpendRefreshToken(Hash(refreshToken), time.GetUtcNow(), cancellationToken) is not { } userId)
        {
            return null;
        }

        var criteria = new UserCriteria().UserId.Eq(userId)
            .WithProjection(new User { Id = Mark.Int64, Name = Mark.String, Roles = Mark.String });
        var user = (await store.Repositories.Users.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault();
        return user is null ? null : await Issue(userId, user.Name ?? string.Empty, user.Roles, cancellationToken);
    }

    public Task Revoke(string refreshToken, CancellationToken cancellationToken) =>
        store.Security.RevokeRefreshToken(Hash(refreshToken), cancellationToken);

    private async Task<Tokens> Issue(long userId, string name, string? roles, CancellationToken cancellationToken)
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
        await store.Security.SaveRefreshToken(Hash(refresh), userId, now + options.RefreshLifetime, cancellationToken);
        return new Tokens(userId, name, access, refresh, expires, HistoryKeys.Derive(options.HistoryMasterKey, userId), roles);
    }

    private async Task<User?> FindUser(string userName, CancellationToken cancellationToken)
    {
        var criteria = new UserCriteria().UserName.Eq(userName)
            .WithProjection(new User { Id = Mark.Int64, Name = Mark.String, Password = Mark.String, Roles = Mark.String });
        return (await store.Repositories.Users.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault();
    }

    // The database keeps a refresh token's hash: a copy of the table logs nobody in.
    private static string Hash(string refreshToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken)));

    // At most once a minute per instance, on a challenge.
    private async Task Sweep(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var next = Interlocked.Read(ref _nextSweep);
        if (now.UtcTicks < next || Interlocked.CompareExchange(ref _nextSweep, (now + s_sweepEvery).UtcTicks, next) != next)
        {
            return;
        }

        await store.Security.Sweep(now, now - _throttle.ForgetAfter, cancellationToken);
    }

    public sealed record Tokens(long UserId, string Name, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
        string HistoryKey, string? Roles);
}
