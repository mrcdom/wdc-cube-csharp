# Architecture

A Cube application is a tree of **places**. Each place is owned by a **presenter** that holds the logic and the
state of one part of the screen, and publishes what the view should draw in a **scope**. The set of presenters alive
at a moment is called a *face* of the cube; moving from one face to another is a *flip*, requested with a
**FlipIntent**. The point of this split is that everything that decides behavior (what to load, what to show, where
to go) is plain C# with no reference to a rendering technology, so it can be tested without a UI and drawn by Blazor,
Avalonia or anything else.

## Places and the place tree

A `Place` is an immutable node of the tree. It has a name, a parent, and optionally a factory that creates its
presenter. Each place is declared once, by its [keys class](#keys-classes), from the place of the keys it extends:

```csharp
public class RootKeys : CubeKeys
{
    public static Place Place { get; } = Place.Root; // the application itself
    ...
}

public class HomeKeys : RootKeys
{
    public static new Place Place { get; } = RootKeys.Place.Child<RootPresenter>("home", app => new HomePresenter(app));
    ...
}

public sealed class ProductKeys : HomeKeys
{
    public static new Place Place { get; } = HomeKeys.Place.Child<RootPresenter>("product", app => new ProductPresenter(app));
    ...
}
```

In the samples the folders follow the same tree: the root module (`RootKeys`, `RootPresenter`, `RootScope`) sits
directly in `Modules/`, and each place's module is a subfolder of its parent's (`Modules/Home/ProductDetail`).

The application presenter lists them for the navigation:

```csharp
public static IReadOnlyList<Place> AllPlaces { get; } =
    [LoginKeys.Place, HomeKeys.Place, ProductKeys.Place, CartKeys.Place, ReceiptKeys.Place, AdminKeys.Place, AmendKeys.Place];
```

- `Child` nests a place under another (`Place.Create` is the same as `Place.Root.Child`).
- `PathName` joins the names from the top: `ProductKeys.Place.PathName` is `home/product`. This is the path part of the address.
- A name cannot contain `/`, `?`, `&`, `#` or `=`.
- A place created without a factory is *structural*: the navigation walks through it without creating anything.
- `Place.Root` is detached (not part of any path); its address is the empty string. The application presenter
  stands for it.
- The list passed to the application (`AllPlaces`) is every place an address can name. Two places with the same
  path are rejected with `ArgumentException`.
- Because places are immutable, they can be shared by every application instance in the process. What is alive at
  run time belongs to each `CubeApplication`.

Reaching `home/product` means: the application presenter, then the presenter of `home`, then the presenter of
`home/product`, each applied in that order. Presenters on the way stay alive and are reused while the next face
still passes through their place.

## Addresses and FlipIntent

A `FlipIntent` is a request to bring the application to a place. It carries two different kinds of data:

| | Parameters | Attributes |
|---|---|---|
| What | The minimal identity of the state | Context of the assembly |
| Goes to the address | Yes (`path?query`) | Never |
| Types | text, `long`, `double`, `bool`, and lists of them | any object |
| Examples | a product id, a page number, a filter | a scope slot, the presenter above, a row already loaded |

- `intent.ToString()` is the address: `home/product?pr-id=7`. `FlipIntent.Parse` reads one back.
- Setting a parameter to `null` removes it, so the default state stays out of the address.
- Reading never throws: an absent or malformed value comes back as `null` (or the default you pass).
- `app.NewFlipIntent(place)` creates an intent already filled by every live presenter (see `PublishParameters`
  below), so a flip starts from the current state, not from a copy of the address.

The same intent instance travels through every level of the path. That is why parameter and attribute names must be
unique across the whole application, each with a single owner.

## Keys classes

Reading and writing names by hand in every presenter would scatter those names. Instead, each place has a **keys
class**: it declares the place, and it is the only code that knows the place's names, with typed properties over the
intent. Each keys class owns a short prefix, and every name it declares starts with it. Keys of a deeper place
extend the keys of the place above, so the class tree is the place tree, and a deeper presenter reads what the
levels above hand down. A place with no names of its own still has its keys (they declare the place and navigate to
it).

```csharp
public sealed class ProductKeys : HomeKeys
{
    public new const string Prefix = "pr";

    public static new Place Place { get; } = HomeKeys.Place.Child<RootPresenter>("product", app => new ProductPresenter(app));

    private static class P
    {
        public const string ProductId = $"{Prefix}-id";
    }

    public ProductKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }

    /// <summary>A positive id, or null when the address carries none it can use.</summary>
    public long? ProductId
    {
        get => Intent.GetInt64(P.ProductId) is > 0 and var id ? id : null;
        set => Intent.SetParameter(P.ProductId, value);
    }
}
```

The framework's `CubeKeys` is the base: it builds `app.NewFlipIntent(place)` when no intent is given (so a trip
starts from the current state) and exposes `Flip()`. Every navigation reads the same way:
`await new ProductKeys(App) { ProductId = id }.Flip();`, `await new CartKeys(App).Flip();`. Inside `ApplyParameters`
the keys are built over the intent in transit (`new ProductKeys(App, intent)`), and a redirect uses that intent too
(`intent.Redirect(HomeKeys.Place)`). Keys convert and validate on the way in: garbage in the address reads as
"absent", never as an exception. A test can check that each place has one keys class, child of the keys of its
parent, and that prefixes do not repeat (`KeysTests` in the samples).

## Presenters

| Type | Has a place | Created and released by |
|---|---|---|
| `ApplicationPresenter<TScope>` | the root | the host |
| `CubePresenter<TApp, TScope>` | yes | the navigation |
| `Presenter<TApp, TScope>` | no (a panel, a tab) | its owner presenter |

All three own a scope, an `UpdateManager` and the `Action(...)` helpers. `ApplicationPresenter` is also the
`CubeApplication`: it holds the page frame, the services and the history.

A presenter with a place implements three members of `ICubePresenter`:

- **`ApplyParameters(intent, initialization, last)`**: accept the intent and recover the state from its parameters.
  Called on every flip that passes through the place, root first.
  - `initialization` is `true` on the first call after construction, `false` when the presenter is reused.
  - `last` is `true` only when this place is the leaf the intent asks for.
  - Return `true` to let the walk continue; `false` to accept *and stop here*; throw to refuse the whole flip
    (see [navigation](navigation.md)).
- **`PublishParameters(intent)`**: write the minimal identification of the state, enough to recreate it. Called on
  every live presenter, application first, whenever an intent is built from the state (including for the address).
- **`Release()`**: called exactly once when the presenter leaves the face. Overrides must call the base.

```csharp
public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
{
    var keys = new ProductKeys(App, intent);
    if (keys.ProductId is not { } productId)
    {
        await App.FlipToIntent(intent.Redirect(HomeKeys.Place)); // nothing to show
        return false;
    }

    if (initialization)
    {
        Scope.OnAddToCart = Action<int>(OnAddToCart);
        Scope.OnBack = Action(() => new HomeKeys(App).Flip());
    }

    if (productId != _productId)
    {
        await Load(productId); // whether the product exists decides the navigation
    }

    keys.ParentSlot(Scope);
    return true;
}

public override void PublishParameters(FlipIntent intent) => new ProductKeys(App, intent).ProductId = _productId;
```

Child presenters without a place (`Presenter<TApp, TScope>`) are created by their owner, usually during
`initialization`, and the owner calls their `Release()` from its own.

## Scopes and scope slots

A `Scope` is the rendering state of a presenter: observed properties the view reads, and action delegates the view
calls. See [the update cycle](update-cycle.md) for how changes reach the view.

A presenter does not know where its scope is shown. The level above decides, by offering a **slot** through the
intent's attributes. A `ScopeSlot` is just `delegate void ScopeSlot(Scope? scope)`.

- The level above sets the slot on the intent (`keys.ParentSlot = content => Scope.Content = content;`).
- The level below reads it and calls it with its scope, on every `ApplyParameters`, before any wait, so the screen
  appears at once.
- Each level reads the slot it was given, then **republishes** `ParentSlot` with its own content slot for the next
  level. Read first, overwrite after.
- `intent.GetScopeSlot(id)` returns `ScopeSlots.Noop` when nobody offered one.
- When `last` is `true`, nothing is below: the level clears its content slot.
- A presenter that writes into a slot it does not own (a dialog slot) calls it with `null` in `Release()`.

```csharp
// HomePresenter.ApplyParameters
var keys = new HomeKeys(App, intent);
keys.ParentSlot(Scope); // shown in the application's body

if (last)
{
    Scope.Content = null; // the default panels
}
else
{
    keys.ParentSlot = content => Scope.Content = content; // for the level below
    keys.Home = this;                                      // an attribute: the cart lives here
}
```

```mermaid
flowchart TD
    A["RootPresenter (root)<br/>RootScope.Body"] -->|ParentSlot = body| H["HomePresenter (home)<br/>HomeScope.Content"]
    H -->|ParentSlot = content| P["ProductPresenter (home/product)"]
    H -->|ParentSlot = content| C["CartPresenter (home/cart)"]
    A -->|ParentSlot = body| L["LoginPresenter (login)"]
```

## Actions

A scope declares its actions as delegate properties with a placeholder, and the presenter binds them once, inside
`if (initialization)`:

```csharp
public sealed class ProductScope : Scope
{
    public string Name { get; set => Set(ref field, value); } = string.Empty;

    public Func<int, Task> OnAddToCart { get; set; } = ScopeActions.Unbound<int>();
    public Func<Task> OnBack { get; set; } = ScopeActions.Unbound();
}
```

- `ScopeActions.Unbound()` is a no-op that writes a trace warning when called; `ScopeActions.IsUnbound(action)`
  lets a test assert that every action was bound.
- `Action(...)` (on every presenter) wraps a handler so that it runs on the application's dispatcher, does nothing if
  the presenter was already released, and hands an exception to the presenter's `OnActionFailure` instead of faulting
  the caller.
- `OnActionFailure(action, caught)` sends the failure to `CubeApplication.Unexpected` by default. A presenter
  overrides it to show there what that screen knows how to say, and calls the base for the rest, so what it does not
  handle still reaches the application:

  ```csharp
  protected override void OnActionFailure(string action, Exception caught)
  {
      if (caught is DbException or HttpRequestException)
      {
          App.Unexpected($"During execution of {action} action", caught); // the detail, for the log
          Show(CartMessages.Unavailable);                                 // the notice, on the cart
          return;
      }

      base.OnActionFailure(action, caught);
  }
  ```

  Keep in the action what changes its flow (a business refusal shown and the user stays); the hook is for failures any
  action of the screen may meet. A cancellation after the presenter was released is not a failure; any other one (an
  HTTP timeout) is. A hook that throws leaves both exceptions to `Unexpected`.
- When the action ends, if nothing was marked dirty and `AutoUpdate` is on, the root scope is marked dirty; then the
  pending updates are flushed at once and the address is republished (unless a navigation is still running).
- `Mirror<T>(handler)` binds a handler that mirrors typing: it runs on the dispatcher but does not flush or touch the
  address on every key. Write the value back into the scope property the box shows.

## See also

- [Update cycle](update-cycle.md)
- [Dispatcher](dispatcher.md)
- [Navigation](navigation.md)
- [History](history.md)
- [Domain](domain.md), [Persistence](persistence.md), [Transactions](transactions.md),
  [Protected repositories](protected-repositories.md), [Remote](remote.md)
- [Getting started](../getting-started.md)
