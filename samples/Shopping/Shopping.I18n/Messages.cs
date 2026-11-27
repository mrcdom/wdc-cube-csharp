using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wdc.Cube;

namespace Shopping.I18n;

/// <summary>
/// The catalog of one language, as a view translates a <see cref="Message"/> with it: the text of its key, each
/// <c>{placeholder}</c> replaced by its argument (a message argument is translated too).
/// </summary>
/// <remarks>
/// A key missing in the language falls back to the default language, then to the key itself. A key with a
/// <c>count</c> argument and no text of its own is a One/Other pair: <c>common.itemCount</c> is
/// <c>common.itemCountOne</c> for a count of 1, <c>common.itemCountOther</c> otherwise. A placeholder without an
/// argument stays as it is.
/// </remarks>
public sealed partial class Messages
{
    private static readonly ConcurrentDictionary<string, Messages> s_byLanguage = new(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, string> _catalog;
    private readonly IReadOnlyDictionary<string, string> _fallback;

    private Messages(string language, IReadOnlyDictionary<string, string> catalog, IReadOnlyDictionary<string, string> fallback)
    {
        Language = language;
        _catalog = catalog;
        _fallback = fallback;
    }

    /// <summary>The tag of the language (<c>pt-BR</c>).</summary>
    public string Language { get; }

    /// <summary>The default language's catalog.</summary>
    public static Messages Default => For(Languages.Default);

    /// <summary>
    /// The catalog closest to <paramref name="languageTag"/>: the same tag, else the same language in another region
    /// (<c>pt-PT</c> gets <c>pt-BR</c>), else the default.
    /// </summary>
    public static Messages For(string? languageTag)
    {
        var language = Closest(languageTag);
        return s_byLanguage.GetOrAdd(language, l => new Messages(l, Load(l), l == Languages.Default ? new Dictionary<string, string>() : Load(Languages.Default)));
    }

    /// <summary>The text of <paramref name="message"/> in this language; empty for none.</summary>
    public string Text(Message? message)
    {
        if (message is null)
        {
            return string.Empty;
        }

        var pattern = PatternOf(message.Key)
            ?? (message.Args.TryGetValue("count", out var count)
                ? PatternOf(message.Key + (count as string == "1" ? "One" : "Other"))
                : null)
            ?? message.Key;
        return Placeholder().Replace(pattern, match => message.Args.TryGetValue(match.Groups[1].Value, out var value)
            ? value as string ?? Text(value as Message)
            : match.Value);
    }

    private string? PatternOf(string key) => _catalog.GetValueOrDefault(key) ?? _fallback.GetValueOrDefault(key);

    private static string Closest(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Languages.Default;
        }

        var exact = Languages.All.FirstOrDefault(l => string.Equals(l, tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var language = tag.Split('-', '_')[0];
        return Languages.All.FirstOrDefault(l => l.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)) ?? Languages.Default;
    }

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(Messages).Assembly.GetManifestResourceStream($"messages.{language}.json")
            ?? throw new InvalidOperationException($"No catalog for {language}.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().ToDictionary(e => e.Name, e => e.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex Placeholder();
}
