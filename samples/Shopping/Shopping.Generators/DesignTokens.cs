using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Shopping.Generators;

/// <summary>
/// The design tokens (<c>Design/tokens.json</c>, in the W3C format) as CSS custom properties for the web views and as
/// a resource dictionary for Avalonia.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Colors: <c>--primary-container</c> in CSS; in Avalonia a <c>SolidColorBrush</c> keyed <c>PrimaryContainer</c>
/// and its <c>Color</c>, <c>PrimaryContainerColor</c> (for a gradient). A color with alpha is <c>#RRGGBBAA</c> in the source and in CSS, and <c>#AARRGGBB</c> in Avalonia.</item>
/// <item>Dimensions: <c>--space-sm</c>, <c>--radius-md</c>, <c>--font-size-lg</c> in CSS; <c>SpaceSm</c> and
/// <c>FontSizeLg</c> (<c>x:Double</c>) and <c>RadiusMd</c> (<c>CornerRadius</c>) in Avalonia.</item>
/// <item>Font weights: <c>--font-weight-semibold</c> in CSS, <c>FontWeightSemibold</c> in Avalonia. The font family
/// is CSS only: Avalonia keeps its theme's.</item>
/// </list>
/// </remarks>
public static class DesignTokens
{
    private const string Header = "Generated from Design/tokens.json by samples/Shopping/generate.cs: edit the tokens, not this file.";

    public static string Css(string tokensJson)
    {
        var css = new StringBuilder();
        css.Append("/* ").Append(Header).Append(" */\n:root {\n");
        foreach (var (group, name, value) in Read(tokensJson))
        {
            var property = group == "color" ? Kebab(name) : $"{Kebab(group)}-{Kebab(name)}";
            css.Append("  --").Append(property).Append(": ").Append(CssValue(group, value)).Append(";\n");
        }

        return css.Append("}\n").ToString();
    }

    public static string Axaml(string tokensJson)
    {
        var xaml = new StringBuilder();
        xaml.Append("<!-- ").Append(Header).Append(" -->\n")
            .Append("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\n")
            .Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n");
        foreach (var (group, name, value) in Read(tokensJson))
        {
            var key = group == "color" ? Pascal(name) : Pascal(group) + Pascal(name);
            var line = group switch
            {
                "color" => $"<Color x:Key=\"{key}Color\">{ArgbOf(value.GetString()!)}</Color>\n  " +
                    $"<SolidColorBrush x:Key=\"{key}\" Color=\"{{StaticResource {key}Color}}\" />",
                "radius" => $"<CornerRadius x:Key=\"{key}\">{Pixels(value)}</CornerRadius>",
                "fontWeight" => $"<FontWeight x:Key=\"{key}\">{WeightName(value.GetInt32())}</FontWeight>",
                "fontFamily" => null,
                _ => $"<x:Double x:Key=\"{key}\">{Pixels(value)}</x:Double>",
            };
            if (line is not null)
            {
                xaml.Append("  ").Append(line).Append('\n');
            }
        }

        return xaml.Append("</ResourceDictionary>\n").ToString();
    }

    /// <summary>Every token, in the order of the source: its group, its name and its value.</summary>
    private static List<(string Group, string Name, JsonElement Value)> Read(string tokensJson)
    {
        using var document = JsonDocument.Parse(tokensJson);
        var tokens = new List<(string, string, JsonElement)>();
        foreach (var group in document.RootElement.EnumerateObject())
        {
            if (group.Name.StartsWith('$'))
            {
                continue;
            }

            foreach (var token in group.Value.EnumerateObject())
            {
                if (!token.Name.StartsWith('$'))
                {
                    tokens.Add((group.Name, token.Name, token.Value.GetProperty("$value").Clone()));
                }
            }
        }

        return tokens;
    }

    private static string CssValue(string group, JsonElement value) => group switch
    {
        "fontFamily" => string.Join(", ", value.EnumerateArray().Select(f => f.GetString()!.Contains(' ', StringComparison.Ordinal)
            ? $"\"{f.GetString()}\""
            : f.GetString())),
        "fontWeight" => value.GetInt32().ToString(CultureInfo.InvariantCulture),
        _ => value.GetString()!,
    };

    /// <summary><c>#RRGGBBAA</c> becomes <c>#AARRGGBB</c>; <c>#RRGGBB</c> stays.</summary>
    private static string ArgbOf(string color) => color.Length == 9 ? $"#{color[7..9]}{color[1..7]}" : color;

    private static string Pixels(JsonElement value)
    {
        var text = value.GetString()!;
        return text.EndsWith("px", StringComparison.Ordinal)
            ? text[..^2]
            : throw new InvalidOperationException($"A dimension in pixels was expected, not '{text}'.");
    }

    private static string WeightName(int weight) => weight switch
    {
        400 => "Normal",
        500 => "Medium",
        600 => "SemiBold",
        700 => "Bold",
        _ => weight.ToString(CultureInfo.InvariantCulture),
    };

    private static string Kebab(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c))
            {
                text.Append('-').Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}
