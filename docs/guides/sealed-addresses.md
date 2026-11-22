# Sealing addresses

A Cube address carries the identity of the current state in its query (`home/product?pr-id=7`). That is what makes
every screen bookmarkable, but it also shows ids and filters to anyone who reads the address bar, and lets them edit
the values. This guide shows how to seal the query with `AesHistoryCodec`, from the optional `Wdc.Cube.Codec`
project: where to install it, how to give each user their own key at login, what happens to an address sealed with
another key, how to rotate keys, and how it interoperates with the TypeScript version.

How the history uses a codec (what is encoded, when, and what the application keeps seeing) is described in
[History](../concepts/history.md#sealed-addresses). This guide is about putting one to work.

## What changes, and what does not

With a codec installed, the history writes `home/product?_e=AQBa4-iu...` instead of `home/product?pr-id=7`.

- Only the query is sealed. The path stays readable, because the place must be resolved before anything is opened.
- An address with no query (`home/cart`) stays as it is.
- Presenters, keys and tests keep reading and writing plain parameters. `PublishParameters` and `ApplyParameters`
  do not change.
- A plain address still opens, so links made before the codec was installed keep working.
- Sealing is not authorization. An address that opens is one the application wrote, not one whose contents are still
  true or still allowed. Presenters validate what they read, and the repositories enforce who reaches what (see
  [Roles and rules](roles-and-rules.md)).

## 1. Reference the codec

`Wdc.Cube.Codec` is a separate project so that an application that never seals an address does not carry a cipher.
It depends only on `Wdc.Cube`. Reference it from the project that installs the codec; in Shopping that is the
presentation (`Shopping.Presentation`), because the application presenter decides when to seal.

The public types are:

| Type | What it is |
|---|---|
| `AesHistoryCodec` | The `IHistoryCodec`: AES-SIV over the query, Base64url on the wire. |
| `HistoryKeyVersion` | A key (32 bytes) and the version byte (1 to 255) that selects it. |
| `Base64Url` | Base64url without padding, the form keys usually travel in. |

## 2. Install it before the first read of the address

`KickStart` reads the address first. If the codec is installed after that, a reload on a sealed address cannot be
opened. Install it before `KickStart`:

```csharp
var history = new MemoryHistoryManager(address);
history.Codec = new AesHistoryCodec(key);   // key: 32 bytes; sealed as version 1
var app = new MainPresenter(history /* , ... */);
await app.KickStart(Places.Main);           // reads the address: the codec is already there
```

`HistoryManager.Codec` can be changed at any moment: set it to `null` to stop sealing. After changing it outside a
navigation, call `app.UpdateHistory()` so the current address is rewritten in the new form. A navigation right
after the change rewrites it anyway.

## 3. Give each user their own key

A single key for the whole application hides the parameters, but a link sealed for one user then opens for every
other user. Shopping gives each user a key of their own, derived at login. Nothing is stored, the same user always
gets the same key (so their bookmarks keep working), and a link copied to another user does not open for them.

**Derive the key from a master key.** The host keeps one master key and derives each user's key from it with
HKDF-SHA256:

```csharp
// samples/Shopping/Shopping.Domain/Users/HistoryKeys.cs
public static string Derive(byte[] master, long userId)
{
    ArgumentNullException.ThrowIfNull(master);
    var info = Encoding.UTF8.GetBytes("wdc-shopping-history:" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, salt: [], info: info);
    return Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

**Hand it out with the login.** The key goes into the session the login opens (`Subject.HistoryKey`):

- In Blazor Server, `LocalAuthentication` receives a function that derives it (`historyKeyOf`), wired in
  `Shopping.Blazor/Program.cs` with `HistoryKeys.Derive(historyMaster, id)`.
- On the REST server, the login and refresh answers carry it as `historyKey` (`Shopping.Server/AuthRoutes.cs`), and
  `RemoteAuthentication` in `Shopping.Client` reads it into the `Subject`. The desktop app gets its key this way.

**Install it whenever the session changes.** `MainPresenter` installs the codec when a session is resumed (before
`KickStart`, so a reload on a sealed address opens), when the user logs in, and removes it on logout:

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Main/MainPresenter.cs
public Task Start() => InvokeAsync(async () =>
{
    // The session first: with it comes the key that opens a sealed address (F5 on one).
    Subject = await ResumeSession();
    InstallCodec(Subject);
    await KickStart(Places.Home);
    if (!HasNavigated)
    {
        await Flip(Places.Home);
    }
});
```

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Main/MainPresenter.cs
private void InstallCodec(Subject? subject)
{
    if (subject?.HistoryKey is not { } key)
    {
        History.Codec = null;
        return;
    }

    try
    {
        History.Codec = new Wdc.Cube.Codec.AesHistoryCodec(Wdc.Cube.Codec.Base64Url.Decode(key));
    }
    catch (PlatformNotSupportedException)
    {
        History.Codec = null;
    }
}
```

`SignIn` calls `InstallCodec(subject)`, and `LoginPresenter` flips to home right after, which writes the first
sealed address. `SignOut` calls `InstallCodec(null)`, so the login screen's address is plain.

**Configure the master key.** Both Shopping hosts read it from `Shopping:History:Key`: Base64, 32 bytes. Outside
Development the host refuses to start without it. In Development a random key is generated at startup, so sealed
links stop opening when the process restarts. Generate a key with any cryptographic random source, for example:

```bash
openssl rand -base64 32
```

Keep it out of source control (user secrets, environment variables or a secret store), as you would a signing key.
Every instance of a host that serves the same users needs the same value, or a link sealed by one instance does not
open on another.

## 4. An address sealed with another key

`AesHistoryCodec.Decode` returns `null` for an address it cannot open: sealed with another key, altered by even one
bit, sealed under a key version no longer offered, or not Base64url at all. The history then drops the parameters
and writes a trace warning; no exception reaches the application. The place in the path opens in its default state.

In Shopping, a product link copied from one user and opened by another lands on the product place without a product
id, which sends the reader back to home:

```csharp
// samples/Shopping/Shopping.Presentation.Tests/SealedAddressTestsBase.cs
var beotrano = await Shop.LoggedIn(db, "beotrano");
var opened = await Shop.Start(db, sealedLink, beotrano.Session);

Assert.NotEqual(productId, opened.Maybe<ProductPresenter>(Places.Product)?.Scope.Id); // the parameters are dropped
```

Two related cases:

- **No codec installed.** A sealed address opened with no codec (a reader who is not logged in, or a WebAssembly
  client) is not decoded at all: the envelope arrives as a parameter named `_e` that no presenter reads, so again
  the place opens without its parameters. In Shopping the home guard sends a reader with no session to the login;
  after the login, they land on home, not on the link.
- **A plain address with a codec installed.** It is read as it is. The next address the application writes is
  sealed.

Design presenters so that a place with no parameters is a valid, if empty, state: redirect, or show a default.
That is what makes a dropped envelope harmless.

## 5. Rotate keys

Each sealed address starts with the version byte of the key that sealed it. A codec seals with its current version
and opens with the current one or any previous version it is given:

```csharp
history.Codec = new AesHistoryCodec(
    new HistoryKeyVersion(2, newKey),          // seals new addresses
    [new HistoryKeyVersion(1, oldKey)]);       // still opens addresses sealed with version 1
```

- Versions are 1 to 255, stable for the life of the key they name. The single-key constructor
  (`new AesHistoryCodec(key)`) is version 1.
- A version offered twice, a version outside 1 to 255, or a key that is not 32 bytes throws `ArgumentException`.
- When you stop offering a version, the addresses it sealed no longer open (they lose their parameters, as above).
  Keep the previous version for as long as you want old bookmarks to work.
- `HistoryKeyVersion.FromText(version, text)` reads a key from its 43 Base64url characters, the form a server hands
  it out in.

With per-user keys, rotate the *master* key: derive each user's key from the new master as the current version, and
from the old master as the previous one. Shopping does not rotate; it hands out a single key per user, which
`InstallCodec` installs as version 1. Rotating there would mean handing out both keys at login and building the
codec with the constructor above.

## 6. What sealing gives, and what it does not

- **Confidential and tamper-proof.** AES-SIV (RFC 5297) is authenticated encryption: the content cannot be read
  without the key, and any change makes the address fail to open.
- **Deterministic.** The same state sealed with the same key version gives the same address. The history does not
  rely on that (it never compares envelopes, only plain text), but it means an observer of the address bar can tell
  that a user came back to the same state, even without reading it.
- **Compressed when it helps.** The query is deflated only when that makes it shorter, which in practice means long
  queries (a short one, below roughly 200 characters, is sealed as it is).
- **No key management.** The codec does not create, store, or transport keys. Where they come from is the
  application's choice; derive, configure and hand them out as in section 3.

## 7. WebAssembly

.NET on WebAssembly has no AES. Constructing `AesHistoryCodec` in the browser throws `PlatformNotSupportedException`.
Catch it and leave addresses plain, as `InstallCodec` does. `Shopping.Wasm` therefore runs with plain addresses,
even though its login receives a key, and it cannot open an address sealed by the Blazor Server host.

## 8. Compatibility with the TypeScript version

`AesHistoryCodec` writes the envelope of the TypeScript implementation's codec:

```
_e=base64url( [version:1][flags:1][V ‖ C] )
```

- `version` is the key version; `flags` bit 0 says the plain text was deflated (raw deflate); `V ‖ C` is the AES-SIV
  output, with the 16-byte synthetic IV first.
- With the same key and version, an address sealed by one implementation opens in the other, and the same state
  seals to the same address in both.
- The envelope parameter is `_e` by default. Both constructors take an `envelope` argument to change it; if you do,
  use the same name on both sides.

`tests/Wdc.Cube.Tests/Codec/AesHistoryCodecTests.cs` holds addresses generated by the TypeScript codec, under two key
versions and including a deflated one, and checks that they open here and that sealing the same state gives the same
address. It also checks the AES-SIV primitive against the RFC 5297 test vector.

## 9. Test sealed addresses

`TestHistoryManager` keeps both forms, so tests can assert on the state and on what travelled:

- `Token` is the last address published, plain.
- `EncodedToken` is the same address as it travelled, sealed when a codec is installed.

```csharp
// samples/Shopping/Shopping.Presentation.Tests/SealedAddressTestsBase.cs
Assert.Equal($"home/product?pr-id={productId}", shop.History.Token); // the state...
Assert.StartsWith("home/product?_e=", shop.History.EncodedToken, StringComparison.Ordinal); // ...the address
Assert.DoesNotContain("pr-id", shop.History.EncodedToken, StringComparison.Ordinal);

var reloaded = await Shop.Start(db, shop.History.EncodedToken, shop.Session); // F5 on the sealed address
Assert.Equal(productId, reloaded.PresenterOf<ProductPresenter>(Places.Product).Scope.Id);
```

The same tests run with the key derived in process (`SealedAddressTests`) and with the key handed out by the server's
login (`SealedAddressHttpTests`). `Shopping.Blazor.Tests/ShoppingPageTests.cs` checks the sealed address in the
rendered page.

## See also

- [History](../concepts/history.md): addresses, Back and Forward, and the codec contract.
- [Navigation](../concepts/navigation.md): what a flip guarantees when a presenter redirects.
- [Roles and rules](roles-and-rules.md): enforcing who reaches what, which sealing does not do.
- [The Shopping sample](../samples/shopping.md)
