namespace Wdc.Cube.Testing;

/// <summary>
/// A history for tests: start the application at any address, record every address it publishes, and simulate the
/// user changing the address (Back, a pasted link).
/// </summary>
public sealed class TestHistoryManager : HistoryManager
{
    private readonly List<string> _tokens = [];
    private string _location;

    /// <summary>Creates the history at <paramref name="location"/> (a cold start or a deep link).</summary>
    public TestHistoryManager(string location = "")
    {
        _location = location;
    }

    /// <summary>The current address. Settable before the application starts.</summary>
    public override string Location => _location;

    /// <summary>The last address published, or empty.</summary>
    public string Token => _tokens.Count > 0 ? _tokens[^1] : string.Empty;

    /// <summary>Every address published, in order, each one recorded only when it changed.</summary>
    public IReadOnlyList<string> Tokens => _tokens;

    /// <summary>How many times the application asked to publish, changed or not.</summary>
    public int UpdateCount { get; private set; }

    /// <summary>Sets the address without telling the application (before it starts).</summary>
    public void SetLocation(string location) => _location = location;

    /// <summary>Changes the address as the user would, and tells the application.</summary>
    public void Navigate(string location)
    {
        _location = location;
        NotifyChanges();
    }

    /// <summary>Forgets the recorded addresses.</summary>
    public void ClearTokens() => _tokens.Clear();

    /// <inheritdoc/>
    public override void Update(CubeApplication app, Place place)
    {
        ArgumentNullException.ThrowIfNull(app);
        UpdateCount++;

        var token = app.NewFlipIntent(place).ToString();
        if (!string.Equals(token, _location, StringComparison.Ordinal) || _tokens.Count == 0)
        {
            _location = token;
            _tokens.Add(token);
        }
    }
}
