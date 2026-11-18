namespace Wdc.Cube.Codec;

/// <summary>One key and the envelope byte that selects it.</summary>
/// <param name="Version">1 to 255, stable for the life of the key it names.</param>
/// <param name="Key">32 bytes.</param>
public sealed record HistoryKeyVersion(int Version, byte[] Key)
{
    /// <summary>A key as the 43 Base64url characters that usually carry it (a server hands it over as text).</summary>
    public static HistoryKeyVersion FromText(int version, string key) => new(version, Base64Url.Decode(key));
}
