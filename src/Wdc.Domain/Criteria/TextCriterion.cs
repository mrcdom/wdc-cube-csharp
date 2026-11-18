namespace Wdc.Domain;

/// <summary>A filter on a text field: adds LIKE, case-insensitive LIKE, containing and starting with.</summary>
public sealed class TextCriterion<TOwner> : ComparableCriterion<TOwner, string?>
    where TOwner : class
{
    /// <summary>The escape character used by <see cref="Containing"/> and <see cref="StartingWith"/>.</summary>
    public const char EscapeChar = '\\';

    public TextCriterion(TOwner owner, string name)
        : base(owner, name, ValueKinds.String)
    {
    }

    public override TextCriterion<TOwner> Or()
    {
        base.Or();
        return this;
    }

    public override TextCriterion<TOwner> And()
    {
        base.And();
        return this;
    }

    /// <summary>LIKE with the wildcards the caller wrote.</summary>
    public TOwner Like(string? pattern) => string.IsNullOrEmpty(pattern) ? Owner : Add(Operator.Like, pattern);

    /// <summary>Case-insensitive LIKE with the wildcards the caller wrote.</summary>
    public TOwner ILike(string? pattern) => string.IsNullOrEmpty(pattern) ? Owner : Add(Operator.ILike, pattern);

    /// <summary>Contains <paramref name="fragment"/> literally, ignoring case (its <c>%</c> and <c>_</c> are escaped).</summary>
    public TOwner Containing(string? fragment) =>
        string.IsNullOrEmpty(fragment) ? Owner : ILike($"%{Escape(fragment)}%");

    /// <summary>Starts with <paramref name="prefix"/> literally, ignoring case.</summary>
    public TOwner StartingWith(string? prefix) =>
        string.IsNullOrEmpty(prefix) ? Owner : ILike($"{Escape(prefix)}%");

    /// <summary>Escapes the LIKE wildcards and the escape character itself.</summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new System.Text.StringBuilder(text.Length + 4);
        foreach (var ch in text)
        {
            if (ch is '%' or '_' or EscapeChar)
            {
                sb.Append(EscapeChar);
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
