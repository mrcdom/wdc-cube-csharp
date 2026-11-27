#:project Shopping.Generators/Shopping.Generators.csproj
#:property TargetFrameworks=net11.0
#:property PublishAot=false

// Writes what the Shopping sample generates from its sources: the design tokens (Design/tokens.json) for the web views
// and for Avalonia, and the keys, typed messages and languages of the catalogs (Shopping.I18n/Catalog). Run it after editing a source, from this folder:
//     dotnet build generate.cs && dotnet run --no-build generate.cs
// The tests fail while a generated file is out of date.
using Shopping.Generators;

var here = Path.GetDirectoryName(Path.GetFullPath(args.Length > 0 ? args[0] : "generate.cs"))!;
var tokens = File.ReadAllText(Path.Combine(here, "Design", "tokens.json"));
Write(Path.Combine(here, "Shopping.Blazor.Views", "wwwroot", "tokens.css"), DesignTokens.Css(tokens));
Write(Path.Combine(here, "Shopping.Avalonia", "Tokens.axaml"), DesignTokens.Axaml(tokens));

var i18n = Path.Combine(here, "Shopping.I18n");
var catalogs = Directory.GetFiles(Path.Combine(i18n, "Catalog"), "messages.*.json")
    .ToDictionary(f => Path.GetFileName(f)["messages.".Length..^".json".Length], f => MessageCatalog.Read(File.ReadAllText(f)));
if (MessageCatalog.Check(catalogs) is { Count: > 0 } problems)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, problems));
    return 1;
}

var reference = catalogs[MessageCatalog.DefaultLanguage];
Write(Path.Combine(i18n, "MessageKeys.cs"), MessageCatalog.Keys(reference));
Write(Path.Combine(i18n, "Texts.cs"), MessageCatalog.Factories(reference));
Write(Path.Combine(i18n, "Languages.cs"), MessageCatalog.Languages(catalogs.Keys));
return 0;

static void Write(string path, string content)
{
    var changed = !File.Exists(path) || File.ReadAllText(path) != content;
    if (changed)
    {
        File.WriteAllText(path, content);
    }

    Console.WriteLine($"{(changed ? "wrote    " : "unchanged")} {path}");
}
