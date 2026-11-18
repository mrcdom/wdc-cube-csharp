namespace Shopping.Presentation.Infra;

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
