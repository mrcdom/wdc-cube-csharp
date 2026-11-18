using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Shopping.Presentation.Infra;

/// <summary>How the screens write values. In the presentation, so every view writes them the same way.</summary>
public static partial class Formats
{
    private static readonly CultureInfo s_brazil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeZoneInfo s_zone = FindZone();

    /// <summary><c>R$ 1.234,56</c>.</summary>
    public static string Money(decimal value) => "R$ " + value.ToString("N2", s_brazil);

    /// <summary><c>18/11/2026</c>, in Brasília time.</summary>
    public static string Date(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, s_zone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

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

    private static TimeZoneInfo FindZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    [GeneratedRegex(@"<li[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();
}
