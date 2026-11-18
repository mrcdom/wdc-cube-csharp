namespace Wdc.Cube;

/// <summary>
/// Changes the form the query travels in (encrypting, compressing, shortening it). Opt-in: an application installs
/// one on its <see cref="HistoryManager"/> and changes nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Only the query is transformed; the path stays readable, because a place must be resolved before any key exists
/// (a guard needs to know where the reader was going). The whole query becomes one parameter,
/// <c><see cref="Envelope"/>=payload</c>.
/// </para>
/// <para>
/// A codec may be non-deterministic (a random nonce): the framework never compares envelopes, only plain text.
/// Encoding is not authorization and does not replace validating parameters on arrival.
/// </para>
/// </remarks>
public interface IHistoryCodec
{
    /// <summary>The name of the single parameter that carries the encoded query.</summary>
    string Envelope { get; }

    /// <summary>Encodes a plain query; <see langword="null"/> to leave it plain.</summary>
    string? Encode(string queryString);

    /// <summary>Decodes a payload; <see langword="null"/> when it cannot be read (wrong key, altered, old version).</summary>
    string? Decode(string payload);
}
