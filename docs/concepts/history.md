# History

The history is the boundary between the application and whatever keeps its address: the browser's address bar, an
in-memory stack on the desktop, or a test. The application writes the identity of its state there after every
navigation and every guarded action, and follows changes made from outside (Back, Forward, a pasted link) by flipping
to the address. Since an address is just a `FlipIntent` as text, any face of the application can be bookmarked,
reloaded or shared, and the same mechanism restores it. Optionally, a codec seals the query part so that the state
travels unreadable and tamper-proof.

## Address and intent

An address is `path?query`: the place's `PathName` and the intent's parameters.

```
home/product?pr-id=7          the product 7, under home
home?hm-page=2                home, second page of the purchase history
                              (empty) the application itself
```

- **State to address.** `history.Update(app, place)` is called by `app.UpdateHistory()`. Implementations write
  `app.NewFlipIntent(place).ToString()`: an intent filled by every live presenter's `PublishParameters`, application
  first. Attributes never reach the address.
- **Address to state.** `app.NewIntentFromString(address)` parses the text. The path is looked up among the places
  given to the application; the query becomes parameters.
  - An empty path (`""`, or only a query such as `?theme=dark`) is the application itself: it resolves to the root,
    with the parameters it carries. Back to the address the browser started at returns to the root.
  - An unknown path resolves to `LastPlace` (or the root before any navigation), carrying the parameters it had.
- **When it is written.** At the end of every flip (on success and on rollback), and at the end of every guarded
  action unless a flip is still running. After changing something that `PublishParameters` writes outside of an
  action, call `UpdateHistory()` yourself.
- **When it is followed.** `HistoryManager.Changed` (raised through `NotifyChanges()`) makes the application read
  `Location` at that moment and flip there, as a request from outside: it waits for a running navigation, and the
  latest address wins. If the address is the current state, nothing happens. If flipping there fails, the error is
  reported and the application flips back to the previous state.

## HistoryManager

```csharp
public abstract class HistoryManager
{
    public virtual string Location { get; }               // always plain text, path?query
    public event Action<HistoryManager>? Changed;         // changed from outside
    public abstract void Update(CubeApplication app, Place place);
    public IHistoryCodec? Codec { get; set; }
    protected void NotifyChanges();
    // EncodeAddress / DecodeAddress / IsEnvelope for implementations
}
```

| Implementation | Package | Where |
|---|---|---|
| `NavigationHistoryManager` | `Wdc.Cube.Blazor` | The browser's address, relative to the app's base URI. |
| `MemoryHistoryManager` | `Wdc.Cube` | Desktop and mobile: addresses kept in memory, with Back and Forward. |
| `NoopHistoryManager` | `Wdc.Cube` | Applications with no address at all. |
| `TestHistoryManager` | `Wdc.Cube.Testing` | Tests: start anywhere, record what is published, simulate the user. |

### Browser (Blazor)

`NavigationHistoryManager` wraps Blazor's `NavigationManager`. `CubeHost<TApp>` creates it and passes it in
`CubeHostContext.History`.

- `https://host/base/home/product?pr-id=7` is the address `home/product?pr-id=7`. The fragment is ignored.
- Every update that changes the address is a `NavigateTo`, that is, a new browser history entry; Back and Forward
  walk them and reach the application as history changes.
- In Blazor Server, `NavigateTo` is confirmed asynchronously. Addresses written and not confirmed yet are tracked, so
  two updates in a row do not look like the user moving, and `Location` returns the last address written.
- The host page must answer every path under the base URI, and the router must be interactive, so that changing the
  address does not reload the page:

```razor
@page "/"
@page "/{*path}"

<CubeHost TApp="RootPresenter" CreateApplication="Create" Start="app => app.KickStart(RootKeys.Place)">
    <ViewSlot Scope="context.Scope" />
</CubeHost>
```

### Memory (desktop)

`MemoryHistoryManager(location)` starts at `location` (a deep link, a restored address) or empty.

- Each update that changes the address pushes the previous one on the back stack and clears the forward stack.
- `GoBack()` and `GoForward()` move and tell the application, which flips to the stored address. They return
  `false` when there is nowhere to go; `CanGoBack`, `CanGoForward` and `EntriesChanged` drive the buttons.

```csharp
private void OnKey(object? sender, KeyEventArgs e)
{
    if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
    {
        _ = e.Key == Key.Left ? _history.GoBack() : _history.GoForward();
        e.Handled = true;
    }
}
```

### Tests

`TestHistoryManager(location)` starts the application at any address and records what it publishes:

- `Tokens` (every address published, plain, recorded only when the state or its form changed), `Token` (the last),
  `UpdateCount` (how many times the application asked, changed or not).
- `Navigate(address)` changes the address as the user would and notifies the application; `SetLocation(address)`
  changes it silently, before start.
- With a codec, `Token` stays plain for readable assertions and `EncodedToken` / `WireLocation` show what travelled.

```csharp
var history = new TestHistoryManager("todos?tm-showing=1");
var app = new RootPresenter(history, new TutorialService());
await app.KickStart(RootKeys.Place);
await app.Settle();

Assert.Equal(["todos?tm-showing=1"], history.Tokens);
```

## Sealed addresses

An `IHistoryCodec` installed on the history changes the form the query travels in. Nothing else in the application
changes: presenters and tests keep reading and writing plain parameters.

```csharp
public interface IHistoryCodec
{
    string Envelope { get; }               // the single parameter that carries the payload
    string? Encode(string queryString);    // null: leave it plain
    string? Decode(string payload);        // null: cannot be read
}
```

- **Only the query is encoded.** The path stays readable, because the place must be resolved before any key is
  known (a guard needs to know where the reader was going). `home/product?pr-id=7` travels as
  `home/product?_e=...`.
- **A plain query still opens.** An address without the envelope is read as it is, so old bookmarks keep working.
- **An envelope that cannot be read loses its parameters.** The place opens in its default state; no error is
  raised (a trace warning is written).
- **The framework never compares envelopes**, only plain text, so a codec may be non-deterministic.
- **Install it before `KickStart`**, which reads the address first. After swapping it at run time, call
  `UpdateHistory()` so the address is rewritten in the new form.
- Encoding is not authorization: validate parameters on arrival as always.

### AES-SIV: Wdc.Cube.Codec

`AesHistoryCodec`, in the optional `Wdc.Cube.Codec` package, seals the query with AES-SIV (RFC 5297) and writes it as
`_e=base64url([version][flags][V ‖ C])`. The format is the one of the TypeScript implementation: an address sealed by
one opens in the other with the same key.

- **Key:** 32 bytes. `HistoryKeyVersion.FromText(version, text)` reads one from 43 Base64url characters.
- **Deterministic:** the same state gives the same address under the same key. What leaks is equality: someone
  reading the history can tell the user came back to the same state.
- **Tamper-proof:** an altered address, or one sealed with another key, does not open (`Decode` returns `null`).
- **Compression:** the query is deflated only when that makes it shorter, which in practice means long ones.
- **Rotation:** new addresses use the current key version (1 to 255); previous versions still open old addresses
  until they are no longer offered.

```csharp
history.Codec = new AesHistoryCodec(key);                 // version 1

history.Codec = new AesHistoryCodec(
    new HistoryKeyVersion(2, newKey),                     // seals
    [new HistoryKeyVersion(1, oldKey)]);                  // still opens
```

The codec does not create, store or transport keys. Where they come from is up to the application.

### Per-user keys

In the Shopping sample, each user gets an address key on login, derived on the server from a master key
(`HKDF-SHA256(master, "wdc-shopping-history:" + userId)`): nothing is stored, the same user always gets the same key,
and a link sealed for one user does not open for another (it opens the place without its parameters). The key travels
with the session, so a reload resumes the session — and installs the codec — before `KickStart` reads the address:

```csharp
private void InstallCodec(Subject? subject)
{
    if (subject?.HistoryKey is not { } key)
    {
        History.Codec = null;   // logged out: plain addresses
        return;
    }

    try
    {
        History.Codec = new AesHistoryCodec(Base64Url.Decode(key));
    }
    catch (PlatformNotSupportedException)
    {
        History.Codec = null;   // WebAssembly
    }
}
```

### WebAssembly

.NET on WebAssembly has no AES. Constructing `AesHistoryCodec` in the browser throws
`PlatformNotSupportedException`; catch it and leave the addresses plain, as above. A Blazor WebAssembly client can
still open plain addresses, but not sealed ones.

## See also

- [Architecture](architecture.md)
- [Update cycle](update-cycle.md)
- [Dispatcher](dispatcher.md)
- [Navigation](navigation.md)
- [Domain](domain.md), [Persistence](persistence.md), [Transactions](transactions.md),
  [Protected repositories](protected-repositories.md), [Remote](remote.md)
- [Getting started](../getting-started.md)
