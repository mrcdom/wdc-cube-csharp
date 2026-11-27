# wdc-cube-csharp

[![CI](https://github.com/mrcdom/wdc-cube-csharp/actions/workflows/ci.yml/badge.svg)](https://github.com/mrcdom/wdc-cube-csharp/actions/workflows/ci.yml)

**Cube** is a front-end architecture that keeps application logic out of the view layer. This is its C#/.NET
implementation, alongside the [TypeScript](https://github.com/mrcdom/wdc-cube) and
[Kotlin](https://github.com/mrcdom/wdc-cube-kotlin) ones.

The screens of a Cube application are plain C#: presenters that load data, react to actions and navigate, and scopes
that hold what the screen shows. Views only draw scopes and forward events. So:

- **The whole application is tested without any UI**, the way a user drives it: call the actions, read the scopes,
  check the address.
- **The same presentation runs on Blazor Server, Blazor WebAssembly and Avalonia**, unchanged.
- **Every screen has an address.** Reload, deep links, Back and Forward work without code in the views; addresses
  can be sealed (encrypted per user).
- **Data access is the same locally or over HTTP**: repositories, criteria and transactions keep their contract
  through a REST layer, with rules per role enforced in the repository every host hands to a session.

## A taste

A presenter fills its scope and binds its actions; navigation is filling the target's keys and flipping:

<!-- snippet: list-presenter -->
```csharp
public sealed class ContactListPresenter(ContactsApp app)
    : CubePresenter<ContactsApp, ContactListScope>(app, new ContactListScope())
{
    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new ContactListKeys(App, intent);
        keys.ParentSlot(Scope); // into the application's body

        if (initialization)
        {
            Scope.OnOpen = Action<long>(id => new ContactKeys(App) { ContactId = id }.Flip());
            var contacts = await App.Book.All();
            Scope.Contacts = [.. contacts.Select(c => new ContactRow(c.Id, c.Name))];
        }

        if (last)
        {
            Scope.Detail = null; // the list itself: no contact open
        }
        else
        {
            keys.ParentSlot = detail => Scope.Detail = detail; // the contact shows beside the list
        }

        return true;
    }
}
```

The view draws the scope and nothing else:

<!-- snippet: list-view -->
```razor
@inherits ScopeView<ContactListScope>

<div class="contacts">
    <ul>
        @foreach (var contact in Scope.Contacts)
        {
            <li @key="contact.Id">
                <button @onclick="() => Scope.OnOpen(contact.Id)">@contact.Name</button>
            </li>
        }
    </ul>
    <ViewSlot Scope="Scope.Detail" />
</div>
```

Tests drive the same scopes with no view at all. The full walkthrough, tests included, is in
[Getting started](docs/getting-started.md).

## The pieces

| Piece | Role |
|---|---|
| **Place** | A node in the navigation tree. Places form a hierarchy, and an address is a path through it. |
| **Presenter** | Owns the logic of a place: loads data, reacts to user actions, decides where to navigate. Knows nothing about the view technology. |
| **Scope** | The state a presenter publishes for a view to draw. Changes are batched and flushed to the view. |
| **FlipIntent** | "Go to this place with these parameters": maps one-to-one to the address. |
| **Slot** | How a level hands the level below the place where its scope is shown. |

## Packages

| Project | What it is |
|---|---|
| `Wdc.Cube` | Places, intents, navigation, presenters, scopes, the update pipeline, history. |
| `Wdc.Cube.Testing` | Driving an application in tests, without a view. |
| `Wdc.Cube.Blazor` | Views for Blazor (Server and WebAssembly). |
| `Wdc.Cube.Avalonia` | Views for Avalonia (desktop). |
| `Wdc.Cube.Codec` | Sealed addresses (AES-SIV), compatible with the TypeScript version. |
| `Wdc.Domain` | Entity models, criteria, projections, repository and transaction contracts, protected repositories (rules per role). |
| `Wdc.Persistence` | SQL repositories built from mappings: one statement per fetch, writes in cascade. |
| `Wdc.Persistence.PostgreSql`, `Wdc.Persistence.SqlServer` | The two dialects. |
| `Wdc.Rest`, `Wdc.Rest.Server`, `Wdc.Rest.Client` | Repositories and transactions over HTTP: the same contracts, local or remote. |
| `Wdc.Rest.Testing` | Testing a REST server in memory: the real pipeline, its client, raw requests. |

The libraries are trimming- and AOT-compatible: no runtime reflection in the core and the domain.

## Documentation

The documentation below, with the API reference, is also published as a site:
<https://mrcdom.github.io/wdc-cube-csharp/>.

- [Getting started](docs/getting-started.md): build a small app, step by step.
- [Concepts](docs/README.md#concepts): how each part works and why.
- [Guides](docs/README.md#guides): testing, Blazor, Avalonia, SQL repositories, REST, rules per role, sealed
  addresses, several instances, trimming.
- [Wire protocol](docs/reference/wire-protocol.md) and [decisions](docs/decisions/README.md).
- Samples:
  - [`samples/GettingStarted`](samples/GettingStarted): the app of the tutorial.
  - [`samples/Tutorial`](samples/Tutorial/README.md): a page frame, a restricted area, a list with a detail dialog
    and TodoMVC, in Blazor.
  - [`samples/Shopping`](samples/Shopping/README.md): login, catalogue, cart, purchase, receipt and an
    administration area, on PostgreSQL or SQL Server; in Blazor Server, Blazor WebAssembly and Avalonia, locally or
    through its server.

The criteria, projection and repository routes on the wire are shared with the Kotlin implementation, and sealed
addresses with the TypeScript one.

## Build and test

Requires the .NET 11 SDK (see `global.json`); the libraries target `net10.0` and `net11.0`.

```bash
dotnet build wdc-cube.slnx
source scripts/test-env.sh      # optional: database connection strings for the integration tests
dotnet test --solution wdc-cube.slnx
```

Tests that need a database are skipped, with the reason, when its connection string is missing. See
[CONTRIBUTING](CONTRIBUTING.md) for the databases and the conventions.

## Changes and security

What changed in each version is in the [changelog](CHANGELOG.md). Report vulnerabilities privately, as
[SECURITY.md](SECURITY.md) describes.

## License

[MIT](LICENSE) © 2017-2026 WeDoCode Consultoria e Soluções Avançadas LTDA
