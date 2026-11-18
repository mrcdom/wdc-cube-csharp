using System.Text.Json;

namespace Shopping.Presentation.Infra;

/// <summary>Who is logged in: what the screens need, not the user record.</summary>
/// <param name="Id">The user.</param>
/// <param name="NickName">How the screens greet them.</param>
/// <param name="Credential">What takes the session back after a reload, when the authentication needs one (a refresh
/// token through HTTP); opaque to the presentation.</param>
public sealed record Subject(long Id, string NickName, string? Credential = null);

/// <summary>
/// What survives a reload of the page (F5) for this browser tab. The host installs it: browser session storage in
/// Blazor, memory in tests.
/// </summary>
public interface ISessionStore
{
    Task<string?> Get(string key);

    Task Set(string key, string value);

    Task Remove(string key);
}

/// <summary>A session store in memory (tests, hosts without a browser).</summary>
public sealed class MemorySessionStore : ISessionStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task<string?> Get(string key) => Task.FromResult(_values.GetValueOrDefault(key));

    public Task Set(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task Remove(string key)
    {
        _values.Remove(key);
        return Task.CompletedTask;
    }
}

internal static class SubjectSession
{
    public const string Key = "shopping.subject";

    public static string Write(Subject subject) => JsonSerializer.Serialize(subject);

    public static Subject? Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Subject>(json);
        }
        catch (JsonException)
        {
            return null; // unreadable: as if nobody were logged in
        }
    }
}
