namespace Wdc.Cube;

/// <summary>
/// The history of an application with no address bar (desktop, mobile): the addresses in memory, with back and
/// forward. Each update that changes the address is one entry; going back or forward flips to the stored address.
/// </summary>
public sealed class MemoryHistoryManager : HistoryManager
{
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];
    private string _current;

    /// <param name="location">Where the application starts (a deep link, a restored address); empty for its default.</param>
    public MemoryHistoryManager(string location = "") => _current = location ?? string.Empty;

    public override string Location => DecodeAddress(_current);

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>Raised after an entry is added or the position moves (for back/forward buttons).</summary>
    public event Action? EntriesChanged;

    public override void Update(CubeApplication app, Place place)
    {
        ArgumentNullException.ThrowIfNull(app);
        var plain = app.NewFlipIntent(place).ToString();
        var question = _current.IndexOf('?', StringComparison.Ordinal);
        var currentQuery = question < 0 ? string.Empty : _current[(question + 1)..];
        var wantsEnvelope = Codec is not null && plain.Contains('?', StringComparison.Ordinal);
        if (string.Equals(plain, Location, StringComparison.Ordinal) && wantsEnvelope == IsEnvelope(currentQuery))
        {
            return; // same state, same form (never compare envelopes: a codec may be non-deterministic)
        }

        var address = EncodeAddress(plain);

        if (_current.Length > 0 || _back.Count > 0)
        {
            _back.Add(_current);
        }

        _forward.Clear();
        _current = address;
        EntriesChanged?.Invoke();
    }

    /// <summary>Goes to the previous address; the application flips there.</summary>
    public bool GoBack() => Move(_back, _forward);

    /// <summary>Goes to the next address; the application flips there.</summary>
    public bool GoForward() => Move(_forward, _back);

    private bool Move(List<string> from, List<string> to)
    {
        if (from.Count == 0)
        {
            return false;
        }

        to.Add(_current);
        _current = from[^1];
        from.RemoveAt(from.Count - 1);
        EntriesChanged?.Invoke();
        NotifyChanges();
        return true;
    }
}
