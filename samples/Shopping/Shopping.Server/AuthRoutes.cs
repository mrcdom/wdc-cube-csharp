using System.Text.Json;
using Wdc.Domain;
using Wdc.Rest;
using Wdc.Rest.Server;

namespace Shopping.Server;

/// <summary>
/// <c>GET /api/auth/challenge?userName=</c> → <c>{"nonce","salt","iterations"}</c>;
/// <c>POST /api/auth/login</c> <c>{"userName","nonce","digest"}</c> and <c>POST /api/auth/refresh</c>
/// <c>{"refreshToken"}</c> → <c>{"userId","name","accessToken","refreshToken","expiresAt","historyKey","roles"}</c>;
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
        w.WriteString("roles", tokens.Roles);
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
