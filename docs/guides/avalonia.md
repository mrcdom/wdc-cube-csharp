# Hosting in Avalonia

`Wdc.Cube.Avalonia` draws a Cube application with Avalonia, on the desktop. The presentation is the same project a
Blazor host draws; only the host changes. It brings three pieces: `AvaloniaPresentation.CreateDispatcher()`, which
runs the presentation on Avalonia's UI thread; `ScopeViewLocator`, a data template that picks the control for each
scope type; and `ScopeView`, the base class of those controls, which confirms each render after Avalonia has laid out
and drawn the frame. With no address bar, the history is a `MemoryHistoryManager`, which still gives the application
Back, Forward and deep links. This guide follows `samples/Shopping/Shopping.Avalonia`, the Shopping app on the
desktop, talking to `Shopping.Server` over HTTP. For the reasons behind the pieces, see
[Update cycle](../concepts/update-cycle.md), [Dispatcher](../concepts/dispatcher.md) and
[History](../concepts/history.md).

## 1. The project

```xml
<!-- samples/Shopping/Shopping.Avalonia/Shopping.Avalonia.csproj -->
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="../Shopping.Client/Shopping.Client.csproj" />
  <ProjectReference Include="../../../src/Wdc.Cube.Avalonia/Wdc.Cube.Avalonia.csproj" />
  <PackageReference Include="Avalonia.Desktop" />
  <PackageReference Include="Avalonia.Themes.Fluent" />
  <PackageReference Include="Avalonia.Fonts.Inter" />
</ItemGroup>
```

`Shopping.Client` brings the presentation and the repositories over HTTP; an application with its data in process
would reference its presentation project directly. `AvaloniaUseCompiledBindingsByDefault` makes every binding a
compiled one (section 4).

## 2. Compose the application

The application presenter is built with two host-specific arguments: Avalonia's dispatcher and an in-memory history.

```csharp
// samples/Shopping/Shopping.Avalonia/App.axaml.cs
internal static (MainPresenter Presenter, MemoryHistoryManager History) Compose(ShoppingClient client, string address = "",
    Microsoft.Extensions.Logging.ILogger? logger = null)
{
    var history = new MemoryHistoryManager(address);
    var presenter = new MainPresenter(history, client.Repositories, new MemorySessionStore(),
        dispatcher: AvaloniaPresentation.CreateDispatcher(), logger: logger, authentication: client.Authentication);
    return (presenter, history);
}
```

- `AvaloniaPresentation.CreateDispatcher()` posts to `Dispatcher.UIThread`. Presenters, flushes and views share the
  UI thread, and an `await` in a presenter comes back to it through Avalonia's synchronization context. Do not use
  `ConfigureAwait(false)` in presentation code.
- `MemoryHistoryManager(address)` starts at `address`: empty for the default face, or a deep link, or an address
  saved when the app last closed.
- The session store is in memory: a desktop process has no reload to survive.

The application is started once the framework is ready, and released on shutdown:

```csharp
// samples/Shopping/Shopping.Avalonia/App.axaml.cs
public override void Initialize()
{
    AvaloniaXamlLoader.Load(this);
    DataTemplates.Add(ViewCatalog.Create());
}

public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        Http = new HttpClient { BaseAddress = ServerAddress, Timeout = TimeSpan.FromSeconds(30) };
        var (presenter, history) = Compose(new ShoppingClient(Http));
        desktop.MainWindow = new MainWindow(presenter, history);
        desktop.ShutdownRequested += (_, _) => presenter.Release();
        _ = presenter.Start();
    }

    base.OnFrameworkInitializationCompleted();
}
```

`Initialize` installs the view locator in the application's `DataTemplates` (next section). The window draws the
application's root scope in a `ContentControl`:

```xml
<!-- samples/Shopping/Shopping.Avalonia/Views/MainWindow.axaml -->
<ContentControl x:Name="Body" />
```

```csharp
// samples/Shopping/Shopping.Avalonia/Views/MainWindow.axaml.cs
Body.Content = presenter?.Scope;
```

## 3. Register the views

`ScopeViewLocator` is an `IDataTemplate` that matches any `Scope`. Register a factory for each scope type:

```csharp
// samples/Shopping/Shopping.Avalonia/Views/ViewCatalog.cs
public static ScopeViewLocator Create() => new ScopeViewLocator()
    .Register<MainScope>(() => new MainView())
    .Register<LoginScope>(() => new LoginView())
    .Register<HomeScope>(() => new HomeView())
    .Register<ProductsPanelScope>(() => new ProductsPanelView())
    .Register<PurchasesPanelScope>(() => new PurchasesPanelView())
    .Register<ProductScope>(() => new ProductView())
    .Register<CartScope>(() => new CartView())
    .Register<CartLineScope>(() => new CartLineView())
    .Register<ReceiptScope>(() => new ReceiptView())
    .Register<AdminScope>(() => new AdminView())
    .Register<AmendScope>(() => new AmendView())
    .Register<AmendLineScope>(() => new AmendLineView());
```

Once it is in `DataTemplates`, any `ContentControl` whose `Content` is a scope, and any `ItemsControl` whose items
are scopes, draws them with the registered control.

- **Register every scope that is drawn through a `ContentControl` or an `ItemsControl`**, including panels and
  list rows. The Blazor catalog can leave out the panels, because the Blazor views write those children by name;
  the Avalonia views of the sample show every child scope through a `ContentControl`, so the locator lists them.
- **The match is on the exact type.** The Blazor registry falls back to a base class's view; `ScopeViewLocator`
  does not.
- **A new scope instance gets a new control.** No state of one face leaks into the next.
- A scope with no registered view is drawn as a `TextBlock` with the class `cube-missing-view`, naming the type.

## 4. Write the views

A view is a `UserControl` that derives from `ScopeView` instead. Its `DataContext` is the scope, set by the
locator; declare its type with `x:DataType` so bindings are compiled and checked at build time:

```xml
<!-- samples/Shopping/Shopping.Avalonia/Views/MainView.axaml -->
<cube:ScopeView xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:cube="using:Wdc.Cube.Avalonia"
                xmlns:m="using:Shopping.Presentation.Modules.Main"
                xmlns:v="using:Shopping.Avalonia.Views"
                x:Class="Shopping.Avalonia.Views.MainView"
                x:DataType="m:MainScope">
  <Panel Background="{StaticResource Bg}">
    <ContentControl Content="{Binding Body}" />
    <Border Classes="toast" IsVisible="{Binding ErrorMessage, Converter={x:Static ObjectConverters.IsNotNull}}"
            PointerPressed="OnDismiss">
      <TextBlock x:Name="Toast" Text="{Binding ErrorMessage}" TextWrapping="Wrap" MaxWidth="520" />
    </Border>
  </Panel>
</cube:ScopeView>
```

The code-behind exposes the scope with its type and calls the scope's actions from event handlers. A view only
draws: it never loads, navigates or computes what another view technology would compute the same way.

```csharp
// samples/Shopping/Shopping.Avalonia/Views/CartView.axaml.cs
public sealed partial class CartView : ScopeView
{
    public CartView() => InitializeComponent();

    private CartScope S => (CartScope)Scope!;

    private void OnContinue(object? sender, RoutedEventArgs e) => _ = S.OnContinue();

    private void OnBuy(object? sender, RoutedEventArgs e) => _ = S.OnBuy();
}
```

Actions are bound with `Action(...)` by the presenter, so they report their own errors; the view discards the task.

### How changes reach the bindings

Scopes do not notify on every assignment. `PropertyChanged` is raised **during the flush**, once per changed
property, so all the changes of one update window reach the bindings together. A `ScopeList` raises
`CollectionChanged` as a `Reset` at the flush. Compiled bindings to scope properties and to `ScopeList`s just work.

`ScopeView` attaches itself as the scope's `IScopeView` while it is on screen (from `OnAttachedToVisualTree`, or
when the `DataContext` changes) and detaches when it leaves the visual tree. When a flush hands it the scope, it
posts `MarkRendered()` at `DispatcherPriority.Background`, which runs after the layout and render passes of that
frame: the scope counts as clean only once the screen shows it.

### Slots and lists

- **A slot** is a `ContentControl` bound to the scope property: `<ContentControl Content="{Binding Body}" />`.
- **A list of row scopes** is an `ItemsControl` bound to the `ScopeList`; each row is drawn by its registered
  view, and redraws on its own when one of its properties changes:

  ```xml
  <!-- samples/Shopping/Shopping.Avalonia/Views/CartView.axaml -->
  <ItemsControl x:Name="LinesList" ItemsSource="{Binding Lines}" />
  ```

- **A list of plain values** (records that are not scopes) uses an ordinary item template with its own
  `x:DataType`. The handler finds the row from the button's `DataContext`:

  ```xml
  <!-- samples/Shopping/Shopping.Avalonia/Views/ProductsPanelView.axaml -->
  <ItemsControl.ItemTemplate>
    <DataTemplate x:DataType="m:ProductCard">
      <Button Classes="product-card" Margin="0,0,12,12" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Click="OnOpen">
  ```

  ```csharp
  // samples/Shopping/Shopping.Avalonia/Views/ProductsPanelView.axaml.cs
  private void OnOpen(object? sender, RoutedEventArgs e)
  {
      if (sender is Button { DataContext: ProductCard card })
      {
          _ = S.OnOpenProduct(card.Id);
      }
  }
  ```

### Text boxes

What is being typed belongs to the text box until the presenter needs it. Two patterns appear in the sample:

- **Send on submit.** `LoginView` and `AdminView` read `TextBox.Text` when the user presses Enter or the button,
  and pass it to the action. When the presenter can change the value too (a filter restored from the address),
  the view listens to the scope's `PropertyChanged` for that property and writes it into the box
  (`samples/Shopping/Shopping.Avalonia/Views/AdminView.axaml.cs`).
- **Send while typing.** `AmendView` calls the typing action from `TextChanged`, and writes the scope's value back
  into the box only when it differs, with a flag so that writing it does not call the action again
  (`samples/Shopping/Shopping.Avalonia/Views/AmendView.axaml.cs`).

## 5. Back and Forward

`MemoryHistoryManager` records an entry each time the address changes. `GoBack()` and `GoForward()` move between
them and the application flips to the stored address; both return `false` when there is nowhere to go. The Shopping
window has no navigation bar and maps Alt+Left and Alt+Right:

```csharp
// samples/Shopping/Shopping.Avalonia/Views/MainWindow.axaml.cs
private void OnKey(object? sender, KeyEventArgs e)
{
    if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
    {
        _ = e.Key == Key.Left ? _history.GoBack() : _history.GoForward();
        e.Handled = true;
    }
}
```

For Back and Forward buttons, enable them from `CanGoBack` and `CanGoForward`, and refresh them on the
`EntriesChanged` event. To reopen the app where the user left it, save `history.Location` on shutdown and pass it to
`MemoryHistoryManager` on the next start.

## 6. Run it

```bash
cd samples/Shopping/Shopping.Server
dotnet run -f net11.0          # the API the desktop app talks to

cd samples/Shopping/Shopping.Avalonia
dotnet run                     # or: dotnet run -- --server http://host:port   (or SHOPPING_SERVER=...)
```

See the [Shopping README](../../samples/Shopping/README.md) for the database settings the server needs.

## 7. Headless tests

`samples/Shopping/Shopping.Avalonia.Tests` runs the real desktop app, with its styles and view catalog, in
Avalonia's headless mode (`Avalonia.Headless`), drawn with Skia so frames can be captured as images. It clicks, types
and reads the controls, against `Shopping.Server` in memory. The presentation logic itself is covered by
[presenter tests](testing-presenters.md); these tests prove that the views draw the scopes and drive the actions.

**Build the headless app** from the real `App`:

```csharp
// samples/Shopping/Shopping.Avalonia.Tests/TestAppBuilder.cs
public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont();
```

**Run every test on the UI thread** of one headless session per process. Pass a `Func<Task<T>>`: a `Func<Task>`
would bind to the overload that returns at the first `await`, and the test would pass without running.

```csharp
// samples/Shopping/Shopping.Avalonia.Tests/DesktopTests.cs
private static readonly Lazy<HeadlessUnitTestSession> s_session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)));

public static Task Run(Func<Task> test) => s_session.Value.Dispatch(async () =>
{
    await test();
    return true;
}, TestContext.Current.CancellationToken);
```

**Open the window** with the same `Compose` the app uses, over the test server's HTTP client, and start the
application:

```csharp
var (presenter, history) = App.Compose(new ShoppingClient(http), logger: _log);
var window = new MainWindow(presenter, history) { Width = 1100, Height = 760 };
window.Show();
var desktop = new Desktop(window, presenter, history, _log);
await presenter.Start();
await desktop.Until(() => desktop.All<LoginView>().Any(), "the login screen");
```

**Wait on the screen, pumping frames.** Nothing renders by itself in headless mode. The `Until` helper ticks the
render timer, runs the queued UI jobs and updates the layout, then checks the condition, until a deadline:

```csharp
AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // the render pass, as a real screen would run it
Dispatcher.UIThread.RunJobs();
Window.UpdateLayout();
if (condition())
{
    return;
}
```

Because `ScopeView` confirms renders from a background-priority post, this loop is also what lets scopes become
clean. A test can assert that every scope on screen ended clean, which proves the views confirmed each render:

```csharp
await desktop.Until(() => desktop.All<ScopeView>().All(v => v.Scope!.RenderState == ScopeRenderState.Clean), "clean scopes");
```

Other helpers in the same file: clicks are raised as `Button.ClickEvent`, typing sets `TextBox.Text`, Back is
`desktop.History.GoBack()`, and `Window.CaptureRenderedFrame()` saves a PNG of the frame for inspection. End each
test with `presenter.Release()` and `window.Close()`.

These tests use the Shopping database (through `ShoppingHttpFixture`, shared with the presentation tests) and are
skipped with the reason when its connection string is not set.

## See also

- [Architecture](../concepts/architecture.md): scopes, slots and the place tree.
- [Update cycle](../concepts/update-cycle.md): the flush, `PropertyChanged`, and the render handshake.
- [Dispatcher](../concepts/dispatcher.md): one presentation thread, and why no `ConfigureAwait(false)`.
- [History](../concepts/history.md): `MemoryHistoryManager`, Back and Forward.
- [Remote](../concepts/remote.md): the repositories over HTTP the desktop app uses.
- [Testing presenters](testing-presenters.md): the same application without any view, locally and over HTTP.
- [Hosting in Blazor](blazor.md): the same presentation in the browser.
