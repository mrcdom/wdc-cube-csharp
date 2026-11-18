using System.Text;

namespace Wdc.Cube.Internal;

/// <summary>
/// The <c>application/x-www-form-urlencoded</c> form used by every Cube implementation: space becomes <c>+</c>,
/// letters, digits and <c>.-*_</c> stay as they are, everything else is percent-encoded from UTF-8.
/// A parameter with several values repeats its name (<c>p=1&amp;p=2</c>).
/// </summary>
internal static class QueryString
{
    public static string Build(IEnumerable<KeyValuePair<string, string[]>> parameters)
    {
        var sb = new StringBuilder();
        foreach (var (name, values) in parameters)
        {
            foreach (var value in values)
            {
                if (sb.Length > 0)
                {
                    sb.Append('&');
                }

                Encode(sb, name);
                sb.Append('=');
                Encode(sb, value);
            }
        }

        return sb.ToString();
    }

    public static void Parse(string query, Action<string, string> onPair)
    {
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            var name = Decode(eq < 0 ? part : part[..eq]);
            var value = eq < 0 ? string.Empty : Decode(part[(eq + 1)..]);
            if (name.Length > 0)
            {
                onPair(name, value);
            }
        }
    }

    private static void Encode(StringBuilder sb, string text)
    {
        Span<byte> buffer = stackalloc byte[4];
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.IsAscii && IsUnreserved((char)rune.Value))
            {
                sb.Append((char)rune.Value);
            }
            else if (rune.Value == ' ')
            {
                sb.Append('+');
            }
            else
            {
                var count = rune.EncodeToUtf8(buffer);
                for (var i = 0; i < count; i++)
                {
                    sb.Append('%').Append(buffer[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
    }

    private static string Decode(string text)
    {
        if (text.AsSpan().IndexOfAny('+', '%') < 0)
        {
            return text;
        }

        var bytes = new List<byte>(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '+')
            {
                bytes.Add((byte)' ');
                i++;
            }
            else if (c == '%' && i + 2 < text.Length && IsHex(text[i + 1]) && IsHex(text[i + 2]))
            {
                bytes.Add((byte)((HexValue(text[i + 1]) << 4) | HexValue(text[i + 2])));
                i += 3;
            }
            else
            {
                // A literal run (a lone '%' included) is copied as UTF-8, keeping surrogate pairs together.
                var start = i++;
                while (i < text.Length && text[i] is not ('+' or '%'))
                {
                    i++;
                }

                bytes.AddRange(Encoding.UTF8.GetBytes(text, start, i - start));
            }
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    private static bool IsUnreserved(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '*' or '_';

    private static bool IsHex(char c) => char.IsAsciiHexDigit(c);

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
}
