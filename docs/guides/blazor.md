# Hosting in Blazor

`Wdc.Cube.Blazor` draws a Cube application with Blazor, in both hosting models: interactive server (the presenters
run on the server, one application per circuit) and WebAssembly (the presenters run in the browser). The presentation
project does not change between the two, and does not reference Blazor at all. The host adds four things: a
**registry** that says which component draws each scope type, **views** that derive from `ScopeView<TScope>`,
**slots** (`ViewSlot`) that draw whatever scope a presenter puts in them, and a **`CubeHost`** that creates the
application on Blazor's dispatcher and ties its history to the browser's address. This guide sets them up step by
step, using the samples. For why scopes, slots and addresses work the way they do, see
[Architecture](../concepts/architecture.md) and [History](../concepts/history.md).

## 1. The projects

| Project | References | Contents |
|---|---|---|
| Presentation | `Wdc.Cube` | Places, presenters, scopes, keys. No view technology. |
| Views | Presentation, `Wdc.Cube.Blazor` | The view components, the view catalog, the host page. |
| Host | Views | `Program.cs`, the root components, services. |

The small samples (`samples/GettingStarted/Contacts.Blazor`, `samples/Tutorial/Tutorial.Blazor`) keep views and host
in one project. Shopping splits them so that two hosts share the views: `Shopping.Blazor.Views` is a Razor class
library (`Microsoft.NET.Sdk.Razor`, with `<SupportedPlatform Include="browser" />`) used by both `Shopping.Blazor`
(server) and `Shopping.Wasm` (WebAssembly).

## 2. Write the views

A view is a component that inherits `ScopeView<TScope>`. It reads the scope's properties in the markup and calls the
scope's actions from event handlers. Where a presenter publishes a child scope, the view puts a `ViewSlot`:

```razor
@* samples/Shopping/Shopping.Blazor.Views/MainView.razor *@
@inherits ScopeView<MainScope>

<div class="root">
    <ViewSlot Scope="Scope.Body" />

    @if (Scope.ErrorMessage is { } message)
    {
        <div class="toast" role="alert" @onclick="Scope.OnDismissError">@message</div>
    }
</div>
```

What `ScopeView<TScope>` does for you:

- It attaches itself as the scope's `IScopeView` when its `Scope` parameter is set, and detaches when the scope is
  replaced or the component is disposed.
- When the scope is flushed it re-renders, and in `OnAfterRender` it calls `MarkRendered()`, so the scope counts as
  clean only once the screen shows it (see [the render handshake](../concepts/update-cycle.md#the-render-handshake)).
- **Events do not trigger a render by themselves**, unlike a plain `ComponentBase`: the scope decides when to redraw.
  A guarded action flushes when it ends, so its result shows at once. A view that keeps state of its own (an open
  menu, a draft, focus) calls `StateHasChanged()` itself.
- To release what a view holds, override `Dispose(bool disposing)`.

### Slots, fixed children and lists

- **`<ViewSlot Scope="..." />`** draws a scope whose type is known only at run time, with the component registered
  for it (section 3). It keys the view by scope instance: the same scope redraws in place; another scope instance
  gets a new component, so no state leaks from one face to the next. `Empty` is a `RenderFragment` drawn when the
  scope is `null`.
- **A child view by name** fits a scope whose type is fixed, such as a panel that is always there:

  ```razor
  @* samples/Shopping/Shopping.Blazor.Views/HomeView.razor *@
  @if (Scope.Products is { } products)
  {
      <ProductsPanelView Scope="products" />
  }
  ```

- **Lists** are plain loops. Key the rows so Blazor keeps them in place:

  ```razor
  @* samples/GettingStarted/Contacts.Blazor/Views/ContactListView.razor *@
  @foreach (var contact in Scope.Contacts)
  {
      <li @key="contact.Id">
          <button @onclick="() => Scope.OnOpen(contact.Id)">@contact.Name</button>
      </li>
  }
  ```

### Text boxes

A text box drawn from the scope uses `@bind:get`, `@bind:set` and `@bind:event="oninput"`, with the setter bound to
the scope's typing action (usually a `Mirror` handler in the presenter):

```razor
@* samples/Tutorial/Tutorial.Blazor/Views/SubscriptionsDetailView.razor *@
<input class="email" type="email" @bind:get="Scope.Email"
       @bind:set="Scope.OnEmailChanged" @bind:event="oninput" />
```

Do not write `value="@Scope.Email" @oninput="..."` instead. Only `@bind` tells Blazor that the browser already holds
the typed value; without it, a render that lands between two keystrokes writes the older value back and the caret
jumps.

A value the presenter needs only on submit (a login form, a password) can stay in the view as plain fields and be
passed to the action when the form is sent; `samples/Shopping/Shopping.Blazor.Views/LoginView.razor` does this.

## 3. Register the views

`ViewRegistry.Register<TScope, TView>()` maps a scope type to its component (`TView` must be a
`ScopeView<TScope>`). Keep the list in one place, the view catalog:

```csharp
// samples/Shopping/Shopping.Blazor.Views/ViewCatalog.cs
public static class ViewCatalog
{
    public static void Register(ViewRegistry views)
    {
        ArgumentNullException.ThrowIfNull(views);
        views
            .Register<MainScope, MainView>()
            .Register<LoginScope, LoginView>()
            .Register<HomeScope, HomeView>()
            .Register<ProductScope, ProductView>()
            .Register<CartScope, CartView>()
            .Register<ReceiptScope, ReceiptView>()
            .Register<AdminScope, AdminView>()
            .Register<AmendScope, AmendView>();
    }
}
```

and add it to the services with `AddCubeViews`, which registers the filled `ViewRegistry` as a singleton:

```csharp
builder.Services.AddCubeViews(ViewCatalog.Register);
```

- Register every scope that can land in a `ViewSlot`. Scopes drawn by a child view by name (the panels above) need
  no entry.
- The lookup walks base types: a scope type with no entry of its own is drawn by the view of its nearest registered
  base class.
- A scope with no view at all draws a `<div class="cube-missing-view">` naming the scope type, rather than a blank
  area.

## 4. The host page

One page answers every path under the base URI, because the address belongs to the application: it is the
application, not the router, that turns `home/product?pr-id=7` into a screen. `CubeHost<TApp>` creates the
application, starts it and draws its root scope:

```razor
@* samples/Shopping/Shopping.Blazor.Views/Pages/Shell.razor *@
@page "/"
@page "/{*path}"
@inject ShoppingRepositories Repositories
@inject ISessionStore Session
@inject IAuthentication Authentication
@inject TimeProvider Time
@inject ILoggerFactory Loggers

@* Every path is the same page: the address belongs to Cube. One application per circuit. *@
<CubeHost TApp="MainPresenter" CreateApplication="Create" Start="app => app.Start()">
    <ViewSlot Scope="context.Scope" />
</CubeHost>

@code {
    [Parameter]
    public string? Path { get; set; }

    private MainPresenter Create(CubeHostContext host) =>
        new(host.History, Repositories, Session, host.Dispatcher, Time, Loggers.CreateLogger<MainPresenter>(), Authentication);
}
```

- `CreateApplication` receives a `CubeHostContext` with two things the application must use: `Dispatcher` (Blazor's
  renderer dispatcher, adapted with `BlazorPresentation.CreateDispatcher`) and `History` (a
  `NavigationHistoryManager` over Blazor's `NavigationManager`).
- `Start` runs once, after the application is created; it is usually `app => app.KickStart(MainKeys.Place)`, or an
  entry point of your own that calls it (`app.Start()` in Shopping and GettingStarted).
- `ChildContent` receives the application as `context`; `<ViewSlot Scope="context.Scope" />` draws its root scope.
- When the component goes away (the circuit closes, the page unloads), `CubeHost` releases the application on its
  dispatcher and stops following the address.
- The `Path` parameter only lets the catch-all route bind; the page does not use it.

### An interactive router

Changing the address must not reload the page. Render the router interactively, from the root component:

```razor
@* samples/Shopping/Shopping.Blazor/Components/App.razor (body) *@
<body>
    <Shopping.Blazor.Views.Routes @rendermode="Interactive" />
    <script src="_framework/blazor.web.js"></script>
</body>

@code {
    private static readonly IComponentRenderMode Interactive = new InteractiveServerRenderMode(prerender: false);
}
```

The server samples turn prerendering off. `CubeHost` creates the application when it first receives parameters, so
with prerendering it would also be created for the static pass, which has no interactive circuit and no browser
storage, and then again for the interactive one.

When the pages live in a separate library, as `Shell` does in Shopping, tell the endpoint about that assembly:

```csharp
// samples/Shopping/Shopping.Blazor/Program.cs
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Shopping.Blazor.Views.Pages.Shell).Assembly); // the pages live with the shared views
```

## 5. Blazor Server or WebAssembly

The views, the view catalog and the host page are the same in both models. What changes is where the presenters run,
and therefore the lifetime of the services and where the data comes from.

| | Interactive server (`Shopping.Blazor`) | WebAssembly (`Shopping.Wasm`) |
|---|---|---|
| Presenters run | On the server, one application per circuit | In the browser, one application per page |
| Dispatcher | The circuit's renderer dispatcher | The browser's single thread |
| Per-user services | `AddScoped` (one per circuit, like the application) | `AddSingleton` is fine: one user per process |
| Data and login | In process: repositories protected for the circuit's actor | Through HTTP, with `Shopping.Client`, to the server that served the page |
| Session storage | `ProtectedSessionStorage` (encrypted by ASP.NET Data Protection) | `sessionStorage` through JS interop |
| Sealed addresses | Yes | No: .NET on WebAssembly has no AES |

In Blazor Server, anything that belongs to one user must be scoped, not a singleton, or two circuits would share it:

```csharp
// samples/Shopping/Shopping.Blazor/Program.cs
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(store);
// Each circuit is a session: its own actor, set by the login, and the repositories protected for it.
builder.Services.AddScoped<ActorSource>();
builder.Services.AddScoped(sp => ShoppingProtection.Protect(store.Repositories, sp.GetRequiredService<ActorSource>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISessionStore, BrowserSessionStore>(); // one per circuit, like the application
```

In WebAssembly the root component is the router itself, and the data goes through a client of the server:

```csharp
// samples/Shopping/Shopping.Wasm/Program.cs
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");

// The presentation runs here; the data and the login are on the server that served this page (same origin).
var client = new ShoppingClient(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(client.Repositories);
builder.Services.AddSingleton<IAuthentication>(client.Authentication);
builder.Services.AddSingleton<ISessionStore, SessionStorageStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddCubeViews(ViewCatalog.Register);

await builder.Build().RunAsync();
```

The server that hosts the WebAssembly app must also answer every path, with `index.html`, so that a deep link or a
reload reaches the application instead of a 404:

```csharp
// samples/Shopping/Shopping.Server/Program.cs
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapShoppingApi();
app.MapFallbackToFile("index.html");
```

See [Remote](../concepts/remote.md) for the repositories and transactions over HTTP.

## 6. The address, Back and Forward

`NavigationHistoryManager` keeps the application's address in the browser's address bar, relative to the base URI:
`https://host/base/home/product?pr-id=7` is the address `home/product?pr-id=7`, and the fragment is ignored.

- Every update that changes the address becomes a new browser history entry. Back and Forward walk those entries,
  and the application flips to each address it is handed.
- A link pasted in the address bar, or a `NavigationManager.NavigateTo` from outside the application, works the same
  way.
- In Blazor Server the browser confirms a navigation asynchronously. The history keeps the addresses it wrote and
  has not seen confirmed yet, so its own writes are not mistaken for the user moving.

Nothing of this is in the views. A view never navigates: it calls an action, and the presenter flips.

## 7. Reload (F5) and the session

A reload creates a new application (a new circuit, or a new WebAssembly page) at the same address. The address alone
rebuilds the screen: that is what `PublishParameters` is for. What the address must not carry, such as who is logged
in, needs storage that survives the reload and goes away with the tab: the browser's session storage.

The framework does not define this storage. Shopping declares its own contract in the presentation,
`ISessionStore` (`Get`, `Set`, `Remove`), and each host provides it:

- `Shopping.Blazor` uses `BrowserSessionStore` (`samples/Shopping/Shopping.Blazor/Infra/BrowserSessionStore.cs`),
  over `ProtectedSessionStorage`; a value it cannot read (an old key, a tampered value) reads as absent.
- `Shopping.Wasm` uses `SessionStorageStore` (in its `Program.cs`), calling `sessionStorage.getItem`, `setItem` and
  `removeItem` through `IJSRuntime`; when the browser refuses to store, the session lives on in memory.
- Tests and the desktop app use `MemorySessionStore`.

The application reads the session **before** it reads the address, because the session may decide what the address
means (a guard) and how to read it (a codec):

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Main/MainPresenter.cs
public Task Start() => InvokeAsync(async () =>
{
    // The session first: with it comes the key that opens a sealed address (F5 on one).
    Subject = await ResumeSession();
    InstallCodec(Subject);
    await KickStart(HomeKeys.Place);
    if (!HasNavigated)
    {
        await new HomeKeys(this).Flip();
    }
});
```

Session storage is reached through JS interop, which needs an interactive render: one more reason the server host
turns prerendering off.

## 8. Sealed addresses

To make the query part of the address unreadable and tamper-proof, install an `IHistoryCodec` on the history, such
as `AesHistoryCodec` from the optional `Wdc.Cube.Codec` package. In Blazor the history is `CubeHostContext.History`;
the application reaches it as `History`. Install the codec before `KickStart`, as `Start` above does, and catch
`PlatformNotSupportedException` so that WebAssembly falls back to plain addresses. Shopping gives each user a key at
login and installs it in `InstallCodec`. The [Sealing addresses](sealed-addresses.md) guide walks through the setup, and
[History: sealed addresses](../concepts/history.md#sealed-addresses) explains the format and the keys.

## 9. Testing the views

The presentation is tested without any view (see [Testing presenters](testing-presenters.md)). To check the views
too, render the host page with bUnit (the `bunit` package): its renderer is Blazor's, and its `NavigationManager`
plays the address bar. Register the services and the view catalog as the host does:

```csharp
// samples/Tutorial/Tutorial.Blazor.Tests/TutorialPageTests.cs
private readonly BunitContext _ctx = new();
private readonly TutorialService _service = new();

public TutorialPageTests()
{
    _ctx.Services.AddSingleton<ITutorialService>(_service);
    _ctx.Services.AddCubeViews(ViewCatalog.Register);
}

private NavigationManager Navigation => _ctx.Services.GetRequiredService<NavigationManager>();

private string Address => Navigation.ToBaseRelativePath(Navigation.Uri);
```

Then render `Shell`, click, and wait for the screen, since renders follow the update window:

```csharp
[Fact]
public void Menu_OpensTheToDos_AndTheAddressFollows()
{
    var page = Open();

    Click(page, ".menu button", "To-dos");

    page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".todo-list li").Count));
    Assert.Equal("todos", Address);
    Assert.Equal("2", page.Find(".todo-count strong").TextContent);
}
```

`Navigation.NavigateTo("...")` from the test is the user moving: Back, Forward or a pasted link
(`ChangingTheAddress_FromOutside_BringsTheFace` in the same file). `tests/Wdc.Cube.Blazor.Tests/ScopeViewTests.cs`
shows lower-level checks: that a change reaches the component only after the update window, and that the scope is
clean only after the component rendered.

## See also

- [Getting started](../getting-started.md): a Blazor Server app from scratch.
- [Architecture](../concepts/architecture.md): scopes, slots and the place tree.
- [Update cycle](../concepts/update-cycle.md): when a view redraws and confirms.
- [Dispatcher](../concepts/dispatcher.md): one presentation thread per application.
- [History](../concepts/history.md): addresses, Back and Forward, sealed addresses.
- [Sealing addresses](sealed-addresses.md): the codec, step by step.
- [Remote](../concepts/remote.md): the data through HTTP, for WebAssembly.
- [Testing presenters](testing-presenters.md) and [Hosting in Avalonia](avalonia.md).
- Samples: [`samples/GettingStarted`](../../samples/GettingStarted), [`samples/Tutorial`](../../samples/Tutorial/README.md),
  [`samples/Shopping`](../../samples/Shopping/README.md).
