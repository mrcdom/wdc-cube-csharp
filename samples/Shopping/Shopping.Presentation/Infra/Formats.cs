using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Shopping.I18n;
using Wdc.Cube;

namespace Shopping.Presentation.Infra;

/// <summary>How the screens write values. In the presentation, so every view writes them the same way.</summary>
public static partial class Formats
{
    /// <summary><c>$1,234.56</c>, whatever the culture of the machine.</summary>
    public static string Money(decimal value) => "$" + value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>
    /// The day in UTC (the instant the database stores), written by the language's own pattern (<c>format.date</c>:
    /// <c>11/18/2026</c> in en-US, <c>18/11/2026</c> in pt-BR).
    /// </summary>
    public static Message Date(DateTimeOffset value)
    {
        var day = value.UtcDateTime;
        return Texts.FormatDate(year: day.Year, month: day.Month.ToString("00", CultureInfo.InvariantCulture),
            day: day.Day.ToString("00", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A product description as the screen shows it: the items of an HTML list, or the plain text as a single item.
    /// The view never parses HTML.
    /// </summary>
    public static IReadOnlyList<string> DescriptionItems(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var items = ListItem().Matches(html).Select(m => Clean(m.Groups[1].Value)).Where(t => t.Length > 0).ToList();
        return items.Count > 0 ? items : [Clean(html)];
    }

    /// <summary>The URL the host serves product images from.</summary>
    public static string ProductImageUrl(long productId) =>
        "image/product/" + productId.ToString(CultureInfo.InvariantCulture) + ".png";

    private static string Clean(string fragment) => WebUtility.HtmlDecode(Tag().Replace(fragment, string.Empty)).Trim();

    [GeneratedRegex(@"<li[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();
}
