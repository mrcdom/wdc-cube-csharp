#:project Shopping.Generators/Shopping.Generators.csproj
#:property TargetFrameworks=net11.0
#:property PublishAot=false

// Writes what the Shopping sample generates from its sources in Design/: the design tokens for the web views and for
// Avalonia. Run it after editing a source, from this folder:
//     dotnet build generate.cs && dotnet run --no-build generate.cs
// The tests fail while a generated file is out of date.
using Shopping.Generators;

var here = Path.GetDirectoryName(Path.GetFullPath(args.Length > 0 ? args[0] : "generate.cs"))!;
var tokens = File.ReadAllText(Path.Combine(here, "Design", "tokens.json"));
Write(Path.Combine(here, "Shopping.Blazor.Views", "wwwroot", "tokens.css"), DesignTokens.Css(tokens));
Write(Path.Combine(here, "Shopping.Avalonia", "Tokens.axaml"), DesignTokens.Axaml(tokens));

static void Write(string path, string content)
{
    var changed = !File.Exists(path) || File.ReadAllText(path) != content;
    if (changed)
    {
        File.WriteAllText(path, content);
    }

    Console.WriteLine($"{(changed ? "wrote    " : "unchanged")} {path}");
}
