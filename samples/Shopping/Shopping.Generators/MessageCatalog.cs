using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shopping.Generators;

/// <summary>
/// The message catalogs (<c>Shopping.I18n/Catalog/messages.&lt;tag&gt;.json</c>: key → text with <c>{placeholder}</c>s):
/// checked against each other, and turned into the keys (<c>MessageKeys</c>), one typed factory per key
/// (<c>Texts</c>, so a presenter cannot misspell an argument) and the list of languages (<c>Languages</c>).
/// </summary>
/// <remarks>
/// A key ending in <c>One</c> and its pair ending in <c>Other</c> are one message, chosen by its <c>count</c> argument:
/// <c>common.itemCountOne</c> and <c>common.itemCountOther</c> make <c>Texts.CommonItemCount(count)</c>.
/// </remarks>
public static partial class MessageCatalog
{
    public const string DefaultLanguage = "en-US";

    private const string Header = "// Generated from Catalog/messages.*.json by samples/Shopping/generate.cs: edit the catalogs, not this file.";

    /// <summary>Reads a catalog: its keys in file order.</summary>
    public static IReadOnlyDictionary<string, string> Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            entries[entry.Name] = entry.Value.GetString() ?? throw new InvalidOperationException($"'{entry.Name}' has no text.");
        }

        return entries;
    }

    /// <summary>What is wrong between the catalogs: a key missing from a language, or a placeholder that differs.</summary>
    public static IReadOnlyList<string> Check(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> catalogs)
    {
        var problems = new List<string>();
        var reference = catalogs[DefaultLanguage];
        foreach (var (language, catalog) in catalogs)
        {
            foreach (var key in reference.Keys.Except(catalog.Keys))
            {
                problems.Add($"{language}: '{key}' is missing");
            }

            foreach (var key in catalog.Keys.Except(reference.Keys))
            {
                problems.Add($"{language}: '{key}' is not in {DefaultLanguage}");
            }

            foreach (var key in catalog.Keys.Intersect(reference.Keys))
            {
                if (!Placeholders(catalog[key]).SetEquals(Placeholders(reference[key])))
                {
                    problems.Add($"{language}: '{key}' has the placeholders {{{string.Join("}, {", Placeholders(catalog[key]))}}}, " +
                                 $"{DefaultLanguage} has {{{string.Join("}, {", Placeholders(reference[key]))}}}");
                }
            }
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in MessagesOf(reference))
        {
            if (!names.TryAdd(message.Name, message.Key))
            {
                problems.Add($"'{message.Key}' and '{names[message.Name]}' make the same name, {message.Name}");
            }
        }

        return problems;
    }

    public static string Keys(IReadOnlyDictionary<string, string> reference)
    {
        var code = new StringBuilder();
        code.Append(Header).Append("\nnamespace Shopping.I18n;\n\n/// <summary>The keys of the catalog.</summary>\npublic static class MessageKeys\n{\n");
        var first = true;
        foreach (var message in MessagesOf(reference))
        {
            code.Append(first ? string.Empty : "\n").Append($"    public const string {message.Name} = \"{message.Key}\";\n");
            first = false;
        }

        return code.Append("}\n").ToString();
    }

    public static string Factories(IReadOnlyDictionary<string, string> reference)
    {
        var code = new StringBuilder();
        code.Append(Header).Append("\nusing Wdc.Cube;\n\nnamespace Shopping.I18n;\n\n")
            .Append("/// <summary>One message per key of the catalog, with an argument per placeholder.</summary>\n")
            .Append("public static class Texts\n{\n");
        var first = true;
        foreach (var message in MessagesOf(reference))
        {
            code.Append(first ? string.Empty : "\n").Append($"    /// <summary>{Escape(message.Example)}</summary>\n");
            if (message.Placeholders.Count == 0)
            {
                code.Append($"    public static Message {message.Name}() => new(MessageKeys.{message.Name});\n");
            }
            else
            {
                var parameters = string.Join(", ", message.Placeholders.Select(p => $"object {p}"));
                var args = string.Join(", ", message.Placeholders.Select(p => $"(\"{p}\", {p})"));
                code.Append($"    public static Message {message.Name}({parameters}) =>\n")
                    .Append($"        Message.Of(MessageKeys.{message.Name}, {args});\n");
            }

            first = false;
        }

        return code.Append("}\n").ToString();
    }

    public static string Languages(IEnumerable<string> tags)
    {
        var all = tags.OrderBy(t => t == DefaultLanguage ? 0 : 1).ThenBy(t => t, StringComparer.Ordinal).ToList();
        return $$"""
            {{Header}}
            namespace Shopping.I18n;

            /// <summary>The languages of the catalog; the first is the default.</summary>
            public static class Languages
            {
                public const string Default = "{{DefaultLanguage}}";

                public static IReadOnlyList<string> All { get; } = [{{string.Join(", ", all.Select(t => $"\"{t}\""))}}];
            }

            """;
    }

    private sealed record CatalogMessage(string Key, string Name, IReadOnlyList<string> Placeholders, string Example);

    /// <summary>The messages of the reference catalog in key order, a One/Other pair as one message.</summary>
    private static List<CatalogMessage> MessagesOf(IReadOnlyDictionary<string, string> reference)
    {
        var messages = new List<CatalogMessage>();
        foreach (var key in reference.Keys.Order(StringComparer.Ordinal))
        {
            if (key.EndsWith("Other", StringComparison.Ordinal) && reference.ContainsKey(key[..^5] + "One"))
            {
                continue;
            }

            if (key.EndsWith("One", StringComparison.Ordinal) && reference.TryGetValue(key[..^3] + "Other", out var other))
            {
                var key0 = key[..^3];
                var placeholders = Ordered(reference[key] + " " + other);
                messages.Add(new CatalogMessage(key0, NameOf(key0), placeholders, other));
                continue;
            }

            messages.Add(new CatalogMessage(key, NameOf(key), Ordered(reference[key]), reference[key]));
        }

        return messages;
    }

    private static string NameOf(string key) =>
        string.Concat(key.Split('.').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static HashSet<string> Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Groups[1].Value)];

    private static List<string> Ordered(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct()];

    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex Placeholder();
}
