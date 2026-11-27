using Shopping.Generators;

namespace Shopping.Presentation.Tests;

/// <summary>
/// What samples/Shopping/generate.cs writes is in step with its sources: run the script after editing one (the message
/// says which file is behind).
/// </summary>
public sealed class GeneratedFilesTests
{
    private static readonly string s_shopping = Path.Combine(RepositoryRoot(), "samples", "Shopping");

    [Fact]
    public void TheDesignTokens_AreGenerated()
    {
        var tokens = File.ReadAllText(Path.Combine(s_shopping, "Design", "tokens.json"));

        AssertInStep(Path.Combine("Shopping.Blazor.Views", "wwwroot", "tokens.css"), DesignTokens.Css(tokens));
        AssertInStep(Path.Combine("Shopping.Avalonia", "Tokens.axaml"), DesignTokens.Axaml(tokens));
    }

    [Fact]
    public void TheMessageKeysFactoriesAndLanguages_AreGenerated()
    {
        var catalogs = Catalogs();
        var reference = catalogs[MessageCatalog.DefaultLanguage];

        AssertInStep(Path.Combine("Shopping.I18n", "MessageKeys.cs"), MessageCatalog.Keys(reference));
        AssertInStep(Path.Combine("Shopping.I18n", "Texts.cs"), MessageCatalog.Factories(reference));
        AssertInStep(Path.Combine("Shopping.I18n", "Languages.cs"), MessageCatalog.Languages(catalogs.Keys));
    }

    /// <summary>Every catalog of Shopping.I18n, by language tag.</summary>
    internal static Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs() =>
        Directory.GetFiles(Path.Combine(s_shopping, "Shopping.I18n", "Catalog"), "messages.*.json").ToDictionary(
            f => Path.GetFileName(f)["messages.".Length..^".json".Length],
            f => MessageCatalog.Read(File.ReadAllText(f)));

    [Fact]
    public void AColorWithAlpha_IsWrittenInTheOrderOfEachRenderer()
    {
        const string Tokens = """{"color": {"$type": "color", "toast": {"$value": "#333333E6"}}}""";

        Assert.Contains("--toast: #333333E6;", DesignTokens.Css(Tokens), StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"ToastColor\">#E6333333</Color>", DesignTokens.Axaml(Tokens), StringComparison.Ordinal);
    }

    private static void AssertInStep(string relativePath, string expected)
    {
        var actual = File.ReadAllText(Path.Combine(s_shopping, relativePath)).ReplaceLineEndings("\n");
        Assert.True(actual == expected, $"{relativePath} is out of date: run samples/Shopping/generate.cs.");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "wdc-cube.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (wdc-cube.slnx) was not found above the test's folder.");
    }
}
