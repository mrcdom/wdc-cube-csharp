namespace Wdc.Cube.Testing;

/// <summary>
/// A history for tests: start the application at any address, record every address it publishes, and simulate the
/// user changing the address (Back, a pasted link). With a codec installed, <see cref="Token"/> stays plain (readable
/// assertions) and <see cref="EncodedToken"/> shows what travelled.
/// </summary>
public sealed class TestHistoryManager : HistoryManager
{
    private readonly List<string> _tokens = [];
    private readonly List<string> _encodedTokens = [];
    private string _wire;

    /// <summary>Creates the history at <paramref name="location"/> (a cold start or a deep link), as it travels.</summary>
    public TestHistoryManager(string location = "")
    {
        _wire = location;
    }

    /// <summary>The current address, always plain.</summary>
    public override string Location => DecodeAddress(_wire);

    /// <summary>The current address as it travels (encoded when a codec is installed).</summary>
    public string WireLocation => _wire;

    /// <summary>The last address published, plain, or empty.</summary>
    public string Token => _tokens.Count > 0 ? _tokens[^1] : string.Empty;

    /// <summary>The last address published, as it travelled, or empty.</summary>
    public string EncodedToken => _encodedTokens.Count > 0 ? _encodedTokens[^1] : string.Empty;

    /// <summary>Every address published (plain), in order, each recorded only when the state or its form changed.</summary>
    public IReadOnlyList<string> Tokens => _tokens;

    /// <summary>Every address published, as it travelled.</summary>
    public IReadOnlyList<string> EncodedTokens => _encodedTokens;

    /// <summary>How many times the application asked to publish, changed or not.</summary>
    public int UpdateCount { get; private set; }

    /// <summary>Sets the address (as it travels) without telling the application, before it starts.</summary>
    public void SetLocation(string location) => _wire = location;

    /// <summary>Changes the address (as it travels) as the user would, and tells the application.</summary>
    public void Navigate(string location)
    {
        _wire = location;
        NotifyChanges();
    }

    /// <summary>Forgets the recorded addresses.</summary>
    public void ClearTokens()
    {
        _tokens.Clear();
        _encodedTokens.Clear();
    }

    /// <inheritdoc/>
    public override void Update(CubeApplication app, Place place)
    {
        ArgumentNullException.ThrowIfNull(app);
        UpdateCount++;

        var plain = app.NewFlipIntent(place).ToString();
        var query = plain.Contains('?', StringComparison.Ordinal) ? plain[(plain.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        var currentQuery = _wire.Contains('?', StringComparison.Ordinal) ? _wire[(_wire.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;

        // Compare plain against plain; and notice when only the form changed (a codec installed or removed).
        var sameState = string.Equals(plain, Location, StringComparison.Ordinal);
        var formChanged = (Codec is not null && query.Length > 0) != IsEnvelope(currentQuery);
        if (sameState && !formChanged && _tokens.Count > 0)
        {
            return;
        }

        _wire = EncodeAddress(plain);
        _tokens.Add(plain);
        _encodedTokens.Add(_wire);
    }
}
