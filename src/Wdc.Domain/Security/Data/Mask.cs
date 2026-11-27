namespace Wdc.Domain;

/// <summary>
/// How a textual field is shown to a role that may see it only partially: a document number with only its last
/// digits, a login with only its first letter. The value keeps its length, the hidden characters replaced by
/// <see cref="Fill"/>.
/// </summary>
/// <remarks>
/// A masked field can be read, but not used in a filter or an ordering (that would reveal what the mask hides), and
/// not written by the grant that masks it: a masked value sent back is refused rather than stored over the real one.
/// </remarks>
public sealed class Mask
{
    /// <summary>The character that replaces what a mask hides.</summary>
    public const char Fill = '*';

    private readonly Func<string, string> _apply;

    private Mask(Func<string, string> apply) => _apply = apply;

    public string Apply(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _apply(value);
    }

    /// <summary>Keeps the first <paramref name="count"/> characters and hides the rest (<c>"jdoe"</c> → <c>"j***"</c>).</summary>
    public static Mask KeepFirst(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new Mask(value =>
        {
            var kept = Math.Min(count, value.Length);
            return string.Concat(value.AsSpan(0, kept), new string(Fill, value.Length - kept));
        });
    }

    /// <summary>Keeps the last <paramref name="count"/> characters and hides the rest (<c>"12345678909"</c> → <c>"*********09"</c>).</summary>
    public static Mask KeepLast(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new Mask(value =>
        {
            var kept = Math.Min(count, value.Length);
            return string.Concat(new string(Fill, value.Length - kept), value.AsSpan(value.Length - kept));
        });
    }
}
