namespace Shopping.Presentation.Infra;

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
