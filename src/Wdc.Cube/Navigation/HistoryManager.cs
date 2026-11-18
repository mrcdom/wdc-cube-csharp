namespace Wdc.Cube;

/// <summary>
/// The boundary with whatever keeps the address (a browser bar, a deep link, a test). The application writes the
/// identity of its state through <see cref="Update"/> and follows external changes (Back, a pasted link) through
/// <see cref="Changed"/>.
/// </summary>
public abstract class HistoryManager
{
    /// <summary>The current address, always as plain text (<c>path?query</c>).</summary>
    public virtual string Location => string.Empty;

    /// <summary>Raised when the address changes from outside the application.</summary>
    public event Action<HistoryManager>? Changed;

    /// <summary>
    /// Publishes the identity of the application's state. Implementations usually write
    /// <c>app.NewFlipIntent(place).ToString()</c> when it differs from the current address.
    /// </summary>
    public abstract void Update(CubeApplication app, Place place);

    /// <summary>Tells the application that the address changed from outside.</summary>
    protected void NotifyChanges() => Changed?.Invoke(this);

    /// <summary>
    /// What transforms the query on its way to the address, if anything. Install it before
    /// <c>KickStart</c>, which reads the address first. After swapping it at run time, call
    /// <see cref="CubeApplication.UpdateHistory"/> so the address is written in the new form.
    /// </summary>
    public IHistoryCodec? Codec { get; set; }

    /// <summary>
    /// The query as it should travel: unchanged without a codec or when the codec declines; otherwise one envelope
    /// parameter.
    /// </summary>
    protected string EncodeQuery(string queryString)
    {
        if (Codec is not { } codec || string.IsNullOrEmpty(queryString))
        {
            return queryString;
        }

        var payload = codec.Encode(queryString);
        return payload is null ? queryString : $"{codec.Envelope}={Uri.EscapeDataString(payload)}";
    }

    /// <summary>
    /// The plain query of what travelled. A query without the envelope comes back as it is (an old bookmark still
    /// opens); an envelope that cannot be read loses its parameters (the place opens in its default state).
    /// </summary>
    protected string DecodeQuery(string queryString)
    {
        if (Codec is not { } codec || string.IsNullOrEmpty(queryString))
        {
            return queryString;
        }

        var payload = ReadEnvelope(queryString, codec.Envelope);
        if (payload is null)
        {
            return queryString;
        }

        var decoded = codec.Decode(payload);
        if (decoded is null)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not read the address; its parameters were discarded. envelope={payload}");
            return string.Empty;
        }

        return decoded;
    }

    /// <summary>
    /// Whether this query is an envelope. Asked instead of comparing envelopes, which a non-deterministic codec makes
    /// different every time.
    /// </summary>
    protected bool IsEnvelope(string queryString) =>
        Codec is { } codec && ReadEnvelope(queryString, codec.Envelope) is not null;

    /// <summary>Applies <see cref="EncodeQuery"/> to the query part of <c>path?query</c>.</summary>
    protected string EncodeAddress(string address)
    {
        var (path, query) = Split(address);
        var encoded = EncodeQuery(query);
        return encoded.Length == 0 ? path : $"{path}?{encoded}";
    }

    /// <summary>Applies <see cref="DecodeQuery"/> to the query part of <c>path?query</c>.</summary>
    protected string DecodeAddress(string address)
    {
        var (path, query) = Split(address);
        var decoded = DecodeQuery(query);
        return decoded.Length == 0 ? path : $"{path}?{decoded}";
    }

    private static (string Path, string Query) Split(string address)
    {
        var question = address.IndexOf('?', StringComparison.Ordinal);
        return question < 0 ? (address, string.Empty) : (address[..question], address[(question + 1)..]);
    }

    // An envelope is the whole query and nothing else: one parameter, the codec's own.
    private static string? ReadEnvelope(string queryString, string envelope)
    {
        var prefix = envelope + "=";
        if (!queryString.StartsWith(prefix, StringComparison.Ordinal) || queryString.Contains('&', StringComparison.Ordinal))
        {
            return null;
        }

        return Uri.UnescapeDataString(queryString[prefix.Length..]);
    }
}

/// <summary>A history that keeps nothing: for applications with no address.</summary>
public sealed class NoopHistoryManager : HistoryManager
{
    /// <inheritdoc/>
    public override void Update(CubeApplication app, Place place)
    {
    }
}
