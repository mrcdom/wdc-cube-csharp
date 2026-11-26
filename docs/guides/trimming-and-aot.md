# Trimming and Native AOT

A Cube application often ends up in places where unused code is removed before it ships: a Blazor WebAssembly app is
trimmed on every Release publish, and a desktop app can be published trimmed or as Native AOT. This guide says what the
Cube libraries guarantee for that, how the entity models work without reflection, what your application still has to
do (its own JSON, its views, its bindings), and how to check the result. It only describes what the repository
configures; .NET's own documentation on trimming covers the general rules.

## What the libraries guarantee

Every project under `src/` is marked trimmable and AOT-compatible in the repository's `Directory.Build.props`
(`WdcProjectPath` is the project's folder relative to the repository root, with `/` on every system):

```xml
<!-- Directory.Build.props -->
<PropertyGroup Condition="$(WdcProjectPath.StartsWith('src/'))">
  <IsPackable>true</IsPackable>
  <IsTrimmable>true</IsTrimmable>
  <IsAotCompatible>true</IsAotCompatible>
  ...
</PropertyGroup>
```

- `IsAotCompatible` turns on the trim, AOT and single-file analyzers (`EnableTrimAnalyzer`, `EnableAotAnalyzer`,
  `EnableSingleFileAnalyzer`) for every library build.
- The same file sets `TreatWarningsAsErrors` for all projects, so a library that called an API the analyzers flag
  (reflection over members, `JsonSerializer` without a context, `MakeGenericType`) would not build.
- `IsTrimmable` also marks each assembly as safe to trim, so a publish trims it even in the WebAssembly default mode
  (`TrimMode` `partial`, below).

That covers `Wdc.Cube`, `Wdc.Cube.Blazor`, `Wdc.Cube.Avalonia`, `Wdc.Cube.Codec`, `Wdc.Cube.Testing`, `Wdc.Domain`,
`Wdc.Persistence` and its two dialects, and `Wdc.Rest`, `Wdc.Rest.Client` and `Wdc.Rest.Server`. The samples under
`samples/` are not marked: they are applications, and the next sections say what an application does.

The one place where a library needs the trimmer's help is a Blazor view: Blazor creates components and sets their
parameters by reflection. `ViewRegistry.Register<TScope, TView>()` annotates `TView` with
`[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]`, so every component you register through it is
kept whole.

## How the domain avoids reflection

Nothing in `Wdc.Domain` discovers members at run time. Everything the codecs, the SQL layer and the REST layer need
is handed over as delegates when the model is declared:

```csharp
// samples/Shopping/Shopping.Domain/Purchases/PurchaseItem.cs
public static readonly EntityModel<PurchaseItem> Model = EntityModel.For(() => new PurchaseItem())
    .Key(ValueKinds.Int64, "id", i => i.Id, (i, v) => i.Id = v)
    .Field(ValueKinds.Int32, "amount", i => i.Amount, (i, v) => i.Amount = v)
    .Field(ValueKinds.Decimal, "price", i => i.Price, (i, v) => i.Price = v)
    .Reference("purchase", i => i.Purchase, (i, v) => i.Purchase = v, () => Purchase.Model)
    .Reference("product", i => i.Product, (i, v) => i.Product = v, () => Product.Model)
    .Build();
```

- **Construction**: `EntityModel.For(() => new PurchaseItem())` takes a factory; no `Activator`.
- **Fields**: each one is a name, a getter and a setter. The trimmer sees the property accesses inside the lambdas and
  keeps exactly those properties.
- **Values**: a `ValueKind<T>` from `ValueKinds` (`Int64`, `Decimal`, `Instant`, `Bytes`…) reads and writes JSON and
  database values for its type. Enums go through the generic `ValueKinds.Enum<TEnum>()`, which parses with
  `Enum.TryParse<TEnum>`.
- **Criteria**: each criterion is created in the criteria's constructor (`Comparable(ValueKinds.Int64, "productId")`)
  and kept in a property; named orderings come from the ordering enum through the generic `Enum.GetValues<TOrderBy>()`.
- **JSON**: `JsonEntityCodec<T>` and `RepositoryWire<TEntity, TCriteria>` walk the model's fields with
  `Utf8JsonReader` and `Utf8JsonWriter`; `RestJson` has the helpers. No `JsonSerializer`, no generated context, no
  attributes on your entities.

So an application gets trim-safe entities by writing the model, and needs nothing else for the data that goes through
repositories, the SQL layer or the REST layer.

## What the application must do

### Its own JSON

JSON your application writes outside the repositories (a session record, a settings file, an auth answer) is yours.
Either:

- write it with `Utf8JsonWriter` and read it with `Utf8JsonReader` or `JsonDocument`, as the Shopping client's
  `RemoteAuthentication` and the server's `AuthRoutes` do; or
- use System.Text.Json source generation: a `partial class … : JsonSerializerContext` with `[JsonSerializable(typeof(T))]`,
  and the `JsonSerializer` overloads that take its `JsonTypeInfo<T>`.

A call such as `JsonSerializer.Serialize(value)` without type information is flagged by the trim analyzer (IL2026).

### Blazor WebAssembly

The WebAssembly SDK trims on publish without any setting. For `samples/Shopping/Shopping.Wasm` in Release, the
evaluated defaults are:

| Property | Value | Effect |
|---|---|---|
| `PublishTrimmed` | `true` | The publish trims. |
| `TrimMode` | `partial` | Only assemblies marked trimmable (the framework and the `Wdc.*` libraries) are trimmed; yours are kept whole. |
| `SuppressTrimAnalysisWarnings` | `true` | Trim warnings are not shown. |
| `JsonSerializerIsReflectionEnabledByDefault` | `true` | Reflection-based `JsonSerializer` still works. |

You can read them for your own project with:

```bash
dotnet msbuild path/to/YourApp.Wasm.csproj -p:Configuration=Release \
  -getProperty:PublishTrimmed -getProperty:TrimMode -getProperty:SuppressTrimAnalysisWarnings
```

What to do:

- Register every view through `ViewRegistry.Register<TScope, TView>()` (with `AddCubeViews`), as
  `samples/Shopping/Shopping.Blazor.Views/ViewCatalog.cs` does. Do not build component types from names.
- Keep your own assemblies trim-safe even though `partial` mode keeps them whole: it is what lets you move to
  `<TrimMode>full</TrimMode>` for a smaller download, and to mark your own libraries `<IsTrimmable>true</IsTrimmable>`.
- Look at the warnings at least once (next section): with the defaults above you will not see them.

### Avalonia

Avalonia bindings written as `{Binding Name}` resolve the property by reflection unless they are compiled. The Shopping
desktop app compiles them all:

```xml
<!-- samples/Shopping/Shopping.Avalonia/Shopping.Avalonia.csproj -->
<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
```

```xml
<!-- samples/Shopping/Shopping.Avalonia/Views/CartView.axaml -->
<cube:ScopeView xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:cube="using:Wdc.Cube.Avalonia"
                xmlns:m="using:Shopping.Presentation.Modules.Home.Cart"
                xmlns:v="using:Shopping.Avalonia.Views"
                x:Class="Shopping.Avalonia.Views.CartView"
                x:DataType="m:CartScope">
```

- Give every view and every `DataTemplate` an `x:DataType` (the scope, or the value type a list shows), so each
  binding is checked at build time against the scope's properties.
- Register views with factories, `ScopeViewLocator.Register<TScope>(() => new CartView())`, as
  `samples/Shopping/Shopping.Avalonia/Views/ViewCatalog.cs` does, instead of a locator that finds a view by its type
  name.

### Servers

`Wdc.Rest.Server` is marked AOT-compatible, and its endpoints read and write the bodies themselves. The rest of an
ASP.NET Core server (authentication, your own minimal-API endpoints, configuration binding) follows ASP.NET Core's own
trimming and AOT support. The Shopping server is not published trimmed.

## How to check

1. **Build.** `dotnet build wdc-cube.slnx` builds the libraries with the analyzers on and warnings as errors.
   For your own library, add `<IsTrimmable>true</IsTrimmable>` (and `<IsAotCompatible>true</IsAotCompatible>` if it
   should support Native AOT) to its project file to get the same analyzers.
2. **Publish the WebAssembly app with the warnings visible:**

   ```bash
   dotnet publish path/to/YourApp.Wasm.csproj -c Release -o /tmp/wasm-publish \
     -p:SuppressTrimAnalysisWarnings=false -p:TrimmerSingleWarn=false
   ```

   `TrimmerSingleWarn=false` lists each warning instead of one per assembly. Expect some warnings from the ASP.NET Core
   components framework itself; none should come from `Wdc.*`, and the ones in your code are yours to fix.
3. **Publish a desktop app trimmed** for one runtime:

   ```bash
   dotnet publish path/to/YourApp.Desktop.csproj -c Release -r osx-arm64 --self-contained \
     -p:PublishTrimmed=true -p:TrimmerSingleWarn=false -o /tmp/desktop-publish
   ```

   Use your own runtime identifier (`win-x64`, `linux-x64`…). Add `-p:PublishAot=true` instead of `PublishTrimmed` to
   try Native AOT; it needs the platform's native toolchain.
4. **Run what you published.** A clean publish is necessary, not sufficient: exercise the screens, in particular the
   ones that read or write your own JSON, before shipping.

## Checklist

- [ ] Every entity has a static `EntityModel` built with `EntityModel.For(...)`; nothing finds fields by name at run
      time.
- [ ] JSON outside the repositories uses `Utf8JsonWriter`/`Utf8JsonReader`/`JsonDocument` or a `JsonSerializerContext`.
- [ ] Blazor views are registered with `ViewRegistry.Register<TScope, TView>()`.
- [ ] Avalonia views compile their bindings and declare `x:DataType`; views are registered with factories.
- [ ] A publish with warnings visible shows none from your code.

## See also

- [Domain](../concepts/domain.md): entity models, projections and criteria.
- [Remote](../concepts/remote.md): the bodies that `RepositoryWire` reads and writes.
- [Exposing repositories over HTTP](rest.md): the client a WebAssembly or desktop app uses.
- [Writing a SQL repository](sql-repositories.md)
- [Hosting in Blazor](blazor.md) and [Hosting in Avalonia](avalonia.md): view registration in each host.
- [The Shopping sample](../samples/shopping.md)
- [Contributing](https://github.com/mrcdom/wdc-cube-csharp/blob/main/CONTRIBUTING.md): the conventions behind these guarantees.
