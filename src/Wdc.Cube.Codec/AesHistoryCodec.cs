using System.IO.Compression;
using System.Text;

namespace Wdc.Cube.Codec;

/// <summary>One key and the envelope byte that selects it.</summary>
/// <param name="Version">1 to 255, stable for the life of the key it names.</param>
/// <param name="Key">32 bytes.</param>
public sealed record HistoryKeyVersion(int Version, byte[] Key)
{
    /// <summary>A key as the 43 Base64url characters that usually carry it (a server hands it over as text).</summary>
    public static HistoryKeyVersion FromText(int version, string key) => new(version, Base64Url.Decode(key));
}

/// <summary>
/// The reference <see cref="IHistoryCodec"/> (decision D-20): AES-SIV over the query, Base64url on the wire, the
/// envelope of the TypeScript version, so an address sealed by one opens in the other with the same key.
/// </summary>
/// <remarks>
/// <code>_e=base64url( [version:1][flags:1][V ‖ C] )</code>
/// <list type="bullet">
/// <item><b>Deterministic:</b> the same state gives the same address (within a key version). What it leaks is
/// equality — someone reading the history can tell the reader came back to the same state.</item>
/// <item><b>Rotation:</b> new addresses use <c>current</c>; <c>previous</c> keys still open the old ones.</item>
/// <item><b>Deflate</b> (raw, flag bit 0) only when it makes the address shorter, which in practice means long ones.</item>
/// <item>It does not make, store or transport a key, and it is not authorisation: an address that opens is one the
/// application wrote, not one whose contents are still true.</item>
/// </list>
/// Not available on WebAssembly (no AES there): constructing it throws <see cref="PlatformNotSupportedException"/>.
/// </remarks>
public sealed class AesHistoryCodec : IHistoryCodec
{
    private const byte Deflated = 1;
    private static readonly byte[][] s_associated = [[]]; // the TS version's empty nonce, as one empty AD component

    private readonly int _current;
    private readonly Dictionary<int, byte[]> _keys = [];

    /// <summary>A single key, as version 1.</summary>
    public AesHistoryCodec(byte[] key, string envelope = "_e")
        : this(new HistoryKeyVersion(1, key), [], envelope)
    {
    }

    /// <summary>A current key, and older ones kept for reading during a rotation.</summary>
    public AesHistoryCodec(HistoryKeyVersion current, IEnumerable<HistoryKeyVersion> previous, string envelope = "_e")
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope);
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("The AES history codec needs AES, which WebAssembly does not offer.");
        }

        Envelope = envelope;
        _current = Add(current, "A history key");
        foreach (var older in previous)
        {
            Add(older, $"The history key for version {older.Version}");
        }
    }

    public string Envelope { get; }

    public string? Encode(string queryString)
    {
        ArgumentNullException.ThrowIfNull(queryString);
        var plain = Encoding.UTF8.GetBytes(queryString);
        var (bytes, deflated) = Pack(plain);
        using var siv = new AesSiv(_keys[_current]);
        var sealedBytes = siv.Seal(s_associated, bytes);

        var envelope = new byte[2 + sealedBytes.Length];
        envelope[0] = (byte)_current;
        envelope[1] = deflated ? Deflated : (byte)0;
        sealedBytes.CopyTo(envelope, 2);
        return Base64Url.Encode(envelope);
    }

    public string? Decode(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        try
        {
            var envelope = Base64Url.Decode(payload);
            if (envelope.Length < 2 || !_keys.TryGetValue(envelope[0], out var key))
            {
                return null; // not an envelope, or a version no longer offered: a clean miss, never nonsense
            }

            using var siv = new AesSiv(key);
            var opened = siv.Open(s_associated, envelope.AsSpan(2));
            if (opened is null)
            {
                return null; // wrong key or altered
            }

            var bytes = (envelope[1] & Deflated) == Deflated ? Inflate(opened) : opened;
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception caught) when (caught is FormatException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private int Add(HistoryKeyVersion entry, string label)
    {
        ArgumentNullException.ThrowIfNull(entry.Key);
        if (entry.Version is < 1 or > 255)
        {
            throw new ArgumentException($"{label} version is an integer from 1 to 255; received {entry.Version}.", nameof(entry));
        }

        if (entry.Key.Length != 32)
        {
            throw new ArgumentException($"{label} is 32 bytes; received {entry.Key.Length}.", nameof(entry));
        }

        if (!_keys.TryAdd(entry.Version, (byte[])entry.Key.Clone()))
        {
            throw new ArgumentException($"History key version {entry.Version} is offered twice.", nameof(entry));
        }

        return entry.Version;
    }

    /// <summary>Deflates only when that helps (below ~200 characters it does not).</summary>
    private static (byte[] Bytes, bool Deflated) Pack(byte[] plain)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(plain);
        }

        var packed = buffer.ToArray();
        return packed.Length < plain.Length ? (packed, true) : (plain, false);
    }

    private static byte[] Inflate(byte[] packed)
    {
        using var input = new MemoryStream(packed);
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        return output.ToArray();
    }
}

/// <summary>Base64url without padding (RFC 4648 §5).</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var standard = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(standard.PadRight(standard.Length + ((4 - (standard.Length % 4)) % 4), '='));
    }
}
