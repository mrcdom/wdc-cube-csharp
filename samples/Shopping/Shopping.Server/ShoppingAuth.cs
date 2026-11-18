using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shopping.Domain;
using Shopping.Domain.Users;
using Wdc.Domain;
using Wdc.Rest;
using Wdc.Rest.Server;

namespace Shopping.Server;

/// <summary>Token settings (decision D-19).</summary>
public sealed class ShoppingAuthOptions
{
    public string Issuer { get; set; } = "wdc-shopping";

    /// <summary>The HMAC key that signs the tokens (32 bytes or more).</summary>
    public byte[] Key { get; set; } = RandomNumberGenerator.GetBytes(32);

    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshLifetime { get; set; } = TimeSpan.FromHours(8);

    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The master key the users' address keys derive from (decision D-22).</summary>
    public byte[] HistoryMasterKey { get; set; } = RandomNumberGenerator.GetBytes(32);

    public SymmetricSecurityKey SigningKey => new(Key);
}

/// <summary>What a role may do: <c>entity:operation</c> (read, write, delete) and <c>data:all</c> (no per-user reach).</summary>
public static class Permissions
{
    public const string AllData = "data:all";

    private static readonly Dictionary<Role, string[]> s_grants = new()
    {
        [Role.Admin] = ["*", AllData],
        [Role.Manager] = ["user:read", "product:read", "product:write", "product:delete", "purchase:read", "purchase-item:read", AllData],
        [Role.Customer] = ["user:read", "product:read", "purchase:read", "purchase:write", "purchase-item:read", "purchase-item:write"],
    };

    public static bool Has(ClaimsPrincipal user, string permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        foreach (var claim in user.FindAll(ShoppingTokens.RoleClaim))
        {
            if (Enum.TryParse<Role>(claim.Value, out var role) && s_grants.TryGetValue(role, out var grants)
                && (grants.Contains("*") || grants.Contains(permission)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Throws unless the caller may do <paramref name="operation"/> on <paramref name="entity"/>.</summary>
    public static void Require(ClaimsPrincipal user, string entity, RpcOperation operation)
    {
        var verb = operation switch
        {
            RpcOperation.Insert or RpcOperation.Update => "write",
            RpcOperation.Delete => "delete",
            _ => "read",
        };
        if (!Has(user, $"{entity}:{verb}"))
        {
            throw new AccessDeniedException($"Not allowed: {entity}:{verb}.");
        }
    }

    /// <summary>The caller's id, when their reach is only their own data; <see langword="null"/> when it is everything.</summary>
    public static long? ReachOf(ClaimsPrincipal user) =>
        Has(user, AllData) ? null : ShoppingTokens.UserIdOf(user) ?? throw new AuthenticationException("Authentication required.");
}

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

/// <summary>
/// <c>GET /api/auth/challenge?userName=</c> → <c>{"nonce","salt","iterations"}</c>;
/// <c>POST /api/auth/login</c> <c>{"userName","nonce","digest"}</c> and <c>POST /api/auth/refresh</c>
/// <c>{"refreshToken"}</c> → <c>{"userId","name","accessToken","refreshToken","expiresAt","historyKey"}</c>;
/// <c>POST /api/auth/logout</c> <c>{"refreshToken"}</c> → <c>{"success":true}</c>.
/// </summary>
public static class AuthRoutes
{
    public static void MapShoppingAuth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup("/api/auth");
        group.MapGet("challenge", Challenge);
        group.MapPost("login", Login);
        group.MapPost("refresh", Refresh);
        group.MapPost("logout", Logout);
    }

    private static Task Challenge(HttpContext context) => RestHttp.Respond(context, async () =>
    {
        var userName = context.Request.Query["userName"].ToString();
        if (userName.Length == 0)
        {
            throw new InvalidRequestException("'userName' is required");
        }

        var (nonce, salt, iterations) = await Tokens(context).Challenge(userName, context.RequestAborted);
        return RestJson.Write(w =>
        {
            w.WriteStartObject();
            w.WriteString("nonce", nonce);
            w.WriteString("salt", Convert.ToBase64String(salt));
            w.WriteNumber("iterations", iterations);
            w.WriteEndObject();
        });
    });

    private static Task Login(HttpContext context) => RestHttp.Respond(context, async () =>
    {
        var fields = ReadFields(await RestHttp.ReadBody(context));
        var tokens = await Tokens(context).Login(Field(fields, "userName"), Field(fields, "nonce"), Field(fields, "digest"),
            context.RequestAborted);
        return Answer(tokens ?? throw new AuthenticationException("Usuário ou senha não reconhecido."));
    });

    private static Task Refresh(HttpContext context) => RestHttp.Respond(context, async () =>
    {
        var fields = ReadFields(await RestHttp.ReadBody(context));
        var tokens = await Tokens(context).Refresh(Field(fields, "refreshToken"), context.RequestAborted);
        return Answer(tokens ?? throw new AuthenticationException("The session expired."));
    });

    private static Task Logout(HttpContext context) => RestHttp.Respond(context, async () =>
    {
        var fields = ReadFields(await RestHttp.ReadBody(context));
        Tokens(context).Revoke(Field(fields, "refreshToken"));
        return RepositoryWireSuccess;
    });

    private static readonly byte[] RepositoryWireSuccess = RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WriteBoolean("success", true);
        w.WriteEndObject();
    });

    private static byte[] Answer(ShoppingTokens.Tokens tokens) => RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WriteNumber("userId", tokens.UserId);
        w.WriteString("name", tokens.Name);
        w.WriteString("accessToken", tokens.AccessToken);
        w.WriteString("refreshToken", tokens.RefreshToken);
        w.WriteString("expiresAt", tokens.ExpiresAt);
        w.WriteString("historyKey", tokens.HistoryKey);
        w.WriteEndObject();
    });

    private static ShoppingTokens Tokens(HttpContext context) => context.RequestServices.GetRequiredService<ShoppingTokens>();

    private static Dictionary<string, string> ReadFields(byte[] body)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var reader = RestJson.Start(body);
        RestJson.ReadObject(ref reader, (string name, ref Utf8JsonReader r) =>
        {
            if (r.TokenType == JsonTokenType.String)
            {
                fields[name] = r.GetString()!;
            }
            else
            {
                r.Skip();
            }
        });
        return fields;
    }

    private static string Field(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) && value.Length > 0 ? value : throw new InvalidRequestException($"'{name}' is required");
}
