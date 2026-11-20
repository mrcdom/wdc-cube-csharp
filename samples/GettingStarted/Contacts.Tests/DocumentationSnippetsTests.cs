using System.Text.RegularExpressions;

namespace Contacts.Tests;

/// <summary>
/// The code the documentation shows is this sample's code. A block in a Markdown file preceded by
/// <c>&lt;!-- snippet: name --&gt;</c> must be, line by line, the region between <c>// &lt;snippet:name&gt;</c> and
/// <c>// &lt;/snippet:name&gt;</c> (or the Razor <c>@* … *@</c> form) in a source file of the repository.
/// </summary>
public sealed partial class DocumentationSnippetsTests
{
    private static readonly string s_root = FindRoot();

    public static TheoryData<string, string> Snippets()
    {
        var data = new TheoryData<string, string>();
        foreach (var (document, name) in Cited())
        {
            data.Add(document, name);
        }

        return data;
    }

    private static IEnumerable<(string Document, string Name)> Cited() =>
        from doc in Documents()
        from Match m in DocSnippet().Matches(File.ReadAllText(doc))
        select (Path.GetRelativePath(s_root, doc), m.Groups["name"].Value);

    [Theory]
    [MemberData(nameof(Snippets))]
    public void TheDocumentShowsTheCodeAsItIs(string document, string name)
    {
        var shown = DocSnippet().Matches(File.ReadAllText(Path.Combine(s_root, document)))
            .First(m => m.Groups["name"].Value == name).Groups["code"].Value;
        var sources = Regions().Where(r => r.Name == name).ToList();

        var source = Assert.Single(sources);
        Assert.Equal(Normalize(source.Code), Normalize(shown));
    }

    [Fact]
    public void EveryDocumentSnippet_HasItsRegion()
    {
        var regions = Regions().Select(r => r.Name).ToHashSet();
        var missing = Cited().Where(c => !regions.Contains(c.Name)).ToList();

        Assert.Empty(missing);
    }

    private static IEnumerable<string> Documents() =>
        Directory.EnumerateFiles(Path.Combine(s_root, "docs"), "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(s_root, "README.md"));

    private static IEnumerable<(string Name, string Code)> Regions()
    {
        var folder = Path.Combine(s_root, "samples", "GettingStarted");
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains("/bin/", StringComparison.Ordinal) || file.Contains("/obj/", StringComparison.Ordinal)
                || !(file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".razor", StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (Match m in SourceRegion().Matches(File.ReadAllText(file)))
            {
                yield return (m.Groups["name"].Value, m.Groups["code"].Value);
            }
        }
    }

    /// <summary>Same lines, whatever the common indentation and the line endings.</summary>
    private static string Normalize(string code)
    {
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n').Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..].TrimEnd() : l.TrimEnd()));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "wdc-cube.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("The repository root (wdc-cube.slnx) was not found.");
    }

    [GeneratedRegex(@"<!--\s*snippet:\s*(?<name>[\w-]+)\s*-->\s*\n```\w*\n(?<code>(?:(?!```).)*?)\n?```", RegexOptions.Singleline)]
    private static partial Regex DocSnippet();

    [GeneratedRegex(@"(?://|@\*)\s*<snippet:(?<name>[\w-]+)>(?:\s*\*@)?[^\n]*\n(?<code>.*?)\n[^\n]*(?://|@\*)\s*</snippet:\k<name>>", RegexOptions.Singleline)]
    private static partial Regex SourceRegion();
}
