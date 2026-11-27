# The Shopping sample

`samples/Shopping` is a complete Cube application: a small store with a login, a catalogue, a cart, purchases and
receipts, and an administration area where purchases already made are corrected. It goes from the database to three
hosts (Blazor Server, Blazor WebAssembly and an Avalonia desktop app) with one presentation, and its tests run the
same scenarios in process and over HTTP. This page walks through it: what the app does, how the solution is split,
one feature followed from the button to the table and back, and how to run it.

The concepts behind each piece are explained in [Concepts](../README.md#concepts); this page links to them rather
than repeating them. The screens and the demo data are in US English.

## What the app does

- **Login.** Everything except the login screen requires a session.
- **Home.** A greeting, the product catalogue, and the user's purchase history, paged.
- **Product.** Details of one product and a quantity to add to the cart.
- **Cart.** Lines with quantities that can be changed or removed, a total, and *Buy*.
- **Receipt.** What was bought, with a success banner right after buying, and the corrections the purchase received.
- **Admin.** For `Admin` and `Manager`: the purchases of every customer, and a screen to correct one (an errata),
  kept with who, when and why.

Demo users (the password is the login):

| User | Role | What they can do |
|---|---|---|
| `fulano`, `beotrano` | Customer | Buy; see their own purchases. |
| `gerente` | Manager | Maintain products; correct purchases. |
| `admin` | Admin | Everything a role of the application may do. |
| `suporte` | Super | Maintenance: some business rules are relaxed for it. |

## The solution

| Project | Depends on | What it holds |
|---|---|---|
| `Shopping.Domain` | `Wdc.Domain` | Entities and their models, criteria, repository contracts, policies (`Security/`), password hashing, address keys. |
| `Shopping.Persistence` | `Shopping.Domain`, `Wdc.Persistence.PostgreSql`, `Wdc.Persistence.SqlServer` | Mappings and SQL repositories, the DDL for PostgreSQL and SQL Server, the demo data and product images (`ShoppingStore`). |
| `Shopping.Presentation` | `Shopping.Domain`, `Wdc.Cube`, `Wdc.Cube.Codec` | Places, keys, presenters, scopes, the session (`Infra/`). No view, no database. |
| `Shopping.Blazor.Views` | `Shopping.Presentation`, `Wdc.Cube.Blazor` | Razor views, the host page and CSS, shared by both Blazor hosts. |
| `Shopping.Hosting` | `Shopping.Persistence` | Health checks and metrics (OpenTelemetry), shared by the two web hosts. |
| `Shopping.Blazor` | `Shopping.Blazor.Views`, `Shopping.Persistence` | Blazor Server host: one application per circuit, repositories protected per circuit, image endpoint. |
| `Shopping.Server` | `Shopping.Persistence`, `Wdc.Rest.Server`, `Shopping.Wasm` | REST backend: repositories as routes, remote transactions, JWT login, images; also serves the WebAssembly app. |
| `Shopping.Client` | `Shopping.Presentation`, `Wdc.Rest.Client` | The repositories and the login over HTTP, for hosts that run the presentation themselves. |
| `Shopping.Wasm` | `Shopping.Blazor.Views`, `Shopping.Client` | Blazor WebAssembly host: the presentation in the browser, data through `Shopping.Server`. |
| `Shopping.Avalonia` | `Shopping.Client`, `Wdc.Cube.Avalonia` | Desktop app: the same presentation drawn by Avalonia. |
| `Shopping.Presentation.Tests` | | The presentation without UI, against a real database, in process and over HTTP. |
| `Shopping.Blazor.Tests` | | The pages on Blazor's renderer (bUnit), and the host in memory. |
| `Shopping.Avalonia.Tests` | | The desktop app headless, against the server in memory. |

The dependency rule is the one from [Architecture](../concepts/architecture.md): the presentation does not reference
a rendering technology or a database. It sees `ShoppingRepositories` (a set of repository contracts) and
`IAuthentication`, and each host decides what is behind them:

| Host | Repositories | Login | Address |
|---|---|---|---|
| `Shopping.Blazor` | SQL, protected per circuit | `LocalAuthentication` | Browser, sealed per user |
| `Shopping.Wasm` | `Shopping.Client` over HTTP | `RemoteAuthentication` | Browser, plain |
| `Shopping.Avalonia` | `Shopping.Client` over HTTP | `RemoteAuthentication` | In memory, sealed per user |

## The place tree

Each place is declared by its keys class, from the place of the keys it extends, so the class tree is the place
tree:

```text
RootKeys      (root)        Modules
├─ LoginKeys  login         Modules/Login
└─ HomeKeys   home          Modules/Home
   ├─ ProductKeys  home/product   Modules/Home/ProductDetail
   ├─ CartKeys     home/cart      Modules/Home/Cart
   ├─ ReceiptKeys  home/receipt   Modules/Home/Receipt
   └─ AdminKeys    home/admin     Modules/Home/Admin
      └─ AmendKeys home/admin/amend   Modules/Home/Admin/Amend
```

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Home/ProductDetail/ProductKeys.cs
public static new Place Place { get; } = HomeKeys.Place.Child<RootPresenter>("product", app => new ProductPresenter(app));
```

`RootPresenter.AllPlaces` lists them for the navigation, and the folders of `Shopping.Presentation/Modules` mirror
the tree. `RootPresenter` is the application presenter: it
holds the session (`Subject`), the repositories, the error toast, and installs the address codec. `HomePresenter` is
the guard of everything under `home` (no session: redirect to `login`) and the shell: header, cart badge, and the
catalogue and purchase panels. It also owns the cart (`CartManager`), which lives as long as Home does.

## One feature, end to end: add to cart, buy, see the receipt

### 1. The address and the keys

Opening a product is a flip to `home/product?pr-id=7`. The names of the parameters belong to keys classes, one per
place, each with its own prefix:

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Home/ProductDetail/ProductKeys.cs
public long? ProductId
{
    get => Intent.GetInt64(P.ProductId) is > 0 and var id ? id : null;
    set => Intent.SetParameter(P.ProductId, value);
}
```

`ProductKeys` extends `HomeKeys`, which extends `RootKeys`. That is how a product reads what Home hands down: the
`Home` attribute (the `HomePresenter` itself, so the product can reach the cart) and `ParentSlot` (where to put its
scope). Attributes never reach the address. See [Architecture](../concepts/architecture.md#keys-classes).

### 2. The presenter and the scope

`ProductPresenter.ApplyParameters` reads the id, loads the product (a product that does not exist refuses the
navigation), binds the actions and puts its scope in Home's content slot. Adding to the cart is a scope action:

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Home/ProductDetail/ProductPresenter.cs
private async Task OnAddToCart(int quantity)
{
    if (quantity < 1)
    {
        Scope.ErrorMessage = QuantityBelowOne;
        return;
    }

    var cart = _home?.Cart ?? throw new InvalidOperationException("The product is shown outside Home.");
    cart.Add(_productId!.Value, Scope.Name, _price, quantity);
    await new CartKeys(App).Flip();
}
```

`CartManager.Add` raises `Changed`; Home recomputes the badge in `OnBeforeScopeUpdate`, and `CartPresenter`
reconciles its lines by product, so a line that stays keeps its scope (`CartScope.Lines` is a
`ScopeList<CartLineScope>`). See [Update cycle](../concepts/update-cycle.md).

### 3. The views

Each host draws the same scope. In Blazor, a `ScopeView<CartScope>` binds the buttons to the scope's actions:

```razor
@* samples/Shopping/Shopping.Blazor.Views/CartView.razor *@
<button class="outlined continue" @onclick="Scope.OnContinue">Continue Shopping</button>
@if (Scope.Lines.Count > 0)
{
    <button class="buy" disabled="@Scope.Buying" @onclick="Scope.OnBuy">@(Scope.Buying ? "Buying…" : "Buy")</button>
}
```

In Avalonia, the code-behind of `CartView` does the same (`_ = S.OnBuy()`). Which view draws which scope is declared
once per host: `Shopping.Blazor.Views/ViewCatalog.cs` and `Shopping.Avalonia/Views/ViewCatalog.cs`.

### 4. The use case: buying

*Buy* runs `CartPresenter.OnBuy`, which delegates to the cart and, on success, flips to the receipt with the
purchase id as a parameter and "just bought" as an attribute (so the success banner shows only on this trip, not on
a reload):

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Home/Cart/CartPresenter.cs
var purchaseId = await Cart.Commit(subject);
await new Receipt.ReceiptKeys(App) { PurchaseId = purchaseId, PurchaseMade = true }.Flip();
```

`CartManager.Commit` checks the cart and runs a resource call, `PurchaseCreate`, which builds a `Purchase` with its
`Items` and inserts it in one transaction that the call opens itself:

```csharp
// samples/Shopping/Shopping.Presentation/Modules/Home/CartManager.cs
var id = await new PurchaseCreate(repositories, userId: subject.Id, buyDate: time.GetUtcNow(), lines: [.. _lines]).Run();
```

Every read and write of the presentation is such a call, in a `Calls/` folder next to its presenter
(`ReceiptQuery`, `ProductQuery`, `MyPurchasesPageQuery`, `AmendmentCreate`...): see
[Transactions](../concepts/transactions.md#resource-calls).

The cart is kept on failure. A business error (`InvalidCartItemException`) becomes a message on the cart; a database
or network failure is logged and shown as "unavailable".

### 5. The repository

`repositories.Purchases` is an `IPurchaseRepository`. In Blazor Server it is the SQL `PurchaseRepository`, wrapped
in a protected repository; over HTTP it is `RemotePurchaseRepository`, which posts to `Shopping.Server`, where the
same protected repository runs. Either way, one insert writes the purchase and its items in one transaction, because
the mapping declares the items as a collection the purchase owns:

```csharp
// samples/Shopping/Shopping.Persistence/PurchaseRepository.cs
.Collection("items", "purchase_id", () => PurchaseItemRepository.Mapping, cascadeDelete: true) // resolved lazily
```

`PurchaseRepository.Insert` also refuses a purchase without `BuyDate`: a purchase is made when it is created. See
[Persistence](../concepts/persistence.md#writes-in-cascade) and [Remote](../concepts/remote.md).

### 6. The policy

Before anything is written, the protected repository applies the purchases' data policy (`PurchasePolicy`):
- a customer may create a purchase only on their own behalf, so one with someone else's name on it is refused;
- each item, inserted in cascade, must be creatable and reached under the items' policy.

The business rules (`PurchaseRules`) then replace the price the cart sent with the product's current price, read
through the store. A refusal anywhere rolls back the purchase and every item. See
[Roles and rules](../guides/roles-and-rules.md) for how policies and rules are written.

### 7. The receipt

`ReceiptPresenter` reads the id from `ReceiptKeys`, and loads the purchase with its items and corrections in one
fetch, filtered by the logged-in user as well as by id, so someone else's receipt is the same as one that does not
exist. `PurchaseMade` is read from the attribute and drives `ReceiptScope.NotifySuccess`. Home, told by
`CartManager.Committed`, reloads its panels so the new purchase appears in the history.

### 8. The tests

The scenario is tested without a view, against a real database, by `BuyingTestsBase`:

```csharp
// samples/Shopping/Shopping.Presentation.Tests/BuyingTestsBase.cs
var shop = await Shop.LoggedIn(db, "beotrano");
var product = await OpenProduct(shop, "Italian Design Coffee Maker");
await shop.Do(() => product.Scope.OnAddToCart(2));

await shop.Do(shop.PresenterOf<CartPresenter>(CartKeys.Place).Scope.OnBuy);

var receipt = shop.PresenterOf<ReceiptPresenter>(ReceiptKeys.Place);
Assert.True(receipt.Scope.NotifySuccess);
Assert.Equal("$399.98", receipt.Scope.TotalLabel);
Assert.StartsWith("home/receipt?rc-id=", shop.History.Token, StringComparison.Ordinal);
```

`Shop` (in `Harness/`) starts `RootPresenter` with a `TestHistoryManager` and a `FakeTimeProvider`, and calls the
scope actions as a view would. The base class runs twice, unchanged:

- `BuyingTests` uses `ShoppingDatabaseFixture`: repositories protected in process and `LocalAuthentication`, as
  Blazor Server runs them.
- `BuyingHttpTests` uses `ShoppingHttpFixture`: `Shopping.Server` runs in memory, and the presentation uses
  `Shopping.Client`, as the WebAssembly and desktop apps do.

The same scenario is clicked through the rendered page in `Shopping.Blazor.Tests/ShoppingPageTests.cs`
(`Buying_FromTheCatalogue_ToTheReceipt`), and through the desktop app in `Shopping.Avalonia.Tests/DesktopTests.cs`.

## A second feature worth reading: the errata

A purchase already made never changes for any application role. `Admin` and `Manager` correct one by inserting a
`PurchaseAmendment`: `AdminPresenter` lists the purchases, `AmendPresenter` lets the user lower quantities and give a
reason, and `PurchaseAmendmentRules` validates the correction and applies it to the items, in the insert's
transaction. The buyer sees the correction on the receipt, without the author. It is the worked example in
[Roles and rules](../guides/roles-and-rules.md#10-model-a-correction-as-its-own-operation); its tests are
`ErrataTestsBase` (in process, over HTTP and on SQL Server) and `AdminTestsBase`.

## Sign-in

The login never sends the password: the server issues a challenge (a single-use nonce, with the salt and iterations
of the user's hash) and the client answers with a digest. What the login keeps lives in the database, so any instance
of a host answers it and a restart signs nobody out (`ISecurityStore`, implemented by `SecurityStore`):

| Table | What it holds |
|---|---|
| `auth_nonce` | The challenges in force. A login spends its nonce in one `DELETE`: of two attempts with the same nonce, one wins. |
| `refresh_token` | The hash of each refresh token in force (never the token). A refresh spends it in one `DELETE ... RETURNING` (`OUTPUT` on SQL Server) and gets a new one. |
| `login_failure` | The failed logins in a row per user name, known or not. |
| `data_protection_key` | The Blazor Server host's key ring: a reload on another instance still reads the browser's session storage. |

- **Failed logins** (`LoginThrottle`): five are free; then each attempt waits 30 seconds, doubled at each failure up
  to 15 minutes, even with the right password. A success clears the count, and so does an hour without failures. The
  server answers 429 with `Retry-After`, and the login screen says how long to wait. Both hosts apply it.
- **Requests per address:** the server's `/api/auth` routes take 60 requests a minute per client address
  (`Shopping:Auth:RequestsPerMinute`; 0 turns it off), with ASP.NET's rate limiter.
- Expired challenges and tokens, and old failures, are swept at most once a minute, on a challenge.
- Sign-ins are counted by outcome (`shopping.signins`: `ok`, `refused`, `throttled`).

## Running it

The commands are in the [sample's README](https://github.com/mrcdom/wdc-cube-csharp/blob/main/samples/Shopping/README.md). In short:

```bash
cd samples/Shopping/Shopping.Blazor
dotnet run                     # Blazor Server, http://localhost:5190

cd samples/Shopping/Shopping.Server
dotnet run -f net11.0          # REST server and the WebAssembly app, http://localhost:5200

cd samples/Shopping/Shopping.Avalonia
dotnet run                     # desktop app, talking to Shopping.Server
```

### Choosing the database

Both `Shopping.Blazor` and `Shopping.Server` read `Shopping:Database` from configuration:

| Key | Values |
|---|---|
| `Provider` | `PostgreSql` (default) or `SqlServer` |
| `ConnectionString` | An ADO.NET connection string for that provider |
| `Schema` | The schema the tables go in (default `shopping`) |

The `appsettings.json` of each host points at a local PostgreSQL database. To use another server, override the
settings with user secrets or environment variables rather than editing the file, for example:

```bash
export Shopping__Database__Provider=SqlServer
export Shopping__Database__ConnectionString='<your SQL Server connection string>'
```

On the first start, `ShoppingStore.EnsureCreated` creates the schema, the tables and the demo data, product images
included; on a database made before the sign-in tables, it adds them and leaves the data alone. The database itself
must exist and the user must be allowed to create a schema in it.

Outside Development, the hosts also require `Shopping:History:Key` (the master of the address keys) and, on the
server, `Shopping:Jwt:Key`, both Base64. In Development random keys are used, so sessions and sealed links do not
survive a restart. See [Sealing addresses](../guides/sealed-addresses.md).

### Several instances

`samples/Shopping/docker` runs two instances of each web host behind nginx, with PostgreSQL and Prometheus, and checks
them from outside. What the instances share, and how requests are routed, is in
[Running several instances](../guides/several-instances.md).

### Running the tests

The database tests read a connection string from `WDC_TEST_POSTGRES` (PostgreSQL) and `WDC_TEST_SQLSERVER`
(SQL Server), and each fixture creates a throwaway schema in that database. Without the variable, those tests are
skipped with the reason. `scripts/test-env.sh` exports them from local settings; you can also set them yourself.

```bash
source scripts/test-env.sh
dotnet test --solution wdc-cube.slnx
```

## Where to look next

| To see | Look at |
|---|---|
| Reads and writes as resource calls | The `Calls/` folders, `PurchaseCreate`, `PurchasesPage` |
| A guard that redirects with the intent in transit | `HomePresenter.ApplyParameters` |
| Panels loaded in parallel, without holding the flip | `ProductsPanelPresenter`, `PurchasesPanelPresenter` |
| Paging kept in the address | `HomeKeys.Page`, `PurchasesPanelPresenter` |
| The session surviving a reload | `RootPresenter.Start`, `SubjectSession`, `BrowserSessionStore` |
| Per-user sealed addresses | `RootPresenter.InstallCodec`, `HistoryKeys`, `SealedAddressTestsBase` |
| Rules per role | `ShoppingPolicies` and the `*Policy` files in `Shopping.Domain/Security`, `SecurityTestsBase` |
| Business rules apart from access | `Shopping.Domain/Security/Rules`, `ErrataTestsBase` |
| Repositories as REST routes | `ShoppingApi.MapRepositories` |
| Protecting an extra repository member | `ProtectedProductRepository.FetchImage` |
| Keyboard Back and Forward on the desktop | `Shopping.Avalonia/Views/MainWindow.axaml.cs` |

## See also

- [Getting started](../getting-started.md): the same pieces in a smaller app.
- [Roles and rules](../guides/roles-and-rules.md)
- [Sealing addresses](../guides/sealed-addresses.md)
- [Architecture](../concepts/architecture.md), [Navigation](../concepts/navigation.md),
  [History](../concepts/history.md)
- [Protected repositories](../concepts/protected-repositories.md), [Remote](../concepts/remote.md),
  [Persistence](../concepts/persistence.md)
