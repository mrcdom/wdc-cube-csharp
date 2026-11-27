# Getting started

In this tutorial you build a small contacts app: a list of contacts, and the details of one beside the list. On the
way you meet every piece of Cube: places and addresses, the application presenter, presenters and scopes, keys,
slots, views, and a test that drives the whole app without any UI.

The finished app is in [`samples/GettingStarted`](https://github.com/mrcdom/wdc-cube-csharp/tree/main/samples/GettingStarted). Every code block on this page is
taken from it, and a test checks that they stay the same, so what you read compiles and runs.

```bash
cd samples/GettingStarted/Contacts.Blazor
dotnet run                     # http://localhost:5170
```

Open a contact and look at the address: `contacts/contact?ct-id=2`. Reload the page, or paste the address in a new
tab: the same screen comes back. Back and Forward work. None of that is written in the views.

## 1. The projects

| Project | References | What goes there |
|---|---|---|
| `Contacts.Presentation` | `Wdc.Cube` | Places, presenters, scopes, keys, the service contract. No view technology. |
| `Contacts.Blazor` | `Contacts.Presentation`, `Wdc.Cube.Blazor` | The host and the views. |
| `Contacts.Tests` | `Contacts.Presentation`, `Wdc.Cube.Testing` | The app driven without a view. |

The presentation knows nothing about Blazor: the same project could be drawn by Avalonia (see the Shopping sample).
Until the packages are published, reference the projects of this repository.

The data comes from a small contract the presenters use, `IContactBook` (`All()` and `Find(id)`), with an in-memory
implementation, `ContactBook`. A real app would call a repository or an API there.

## 2. Places and keys: the map of the app

A **place** is a node of the navigation tree, and an address is a path through it. The app has two places: the
list, and a contact below it. Each place is declared by its **keys class**, together with the presenter born there.
A keys class is also the only code that knows the names the place reads and writes in an **intent** (the request
to go somewhere, which becomes the address). Keys of a deeper place extend the keys of the place above, so the
class tree is the place tree.

The application's keys hold the root place and a **slot**, explained in the next section. `CubeKeys`, from the
framework, adds the intent and `Flip()`:

<!-- snippet: root-keys -->
```csharp
/// <summary>
/// The application level: its place (the root) and the names it reads and writes in an intent. The keys of each
/// deeper place extend the keys of the place above, and own a prefix, so names never clash.
/// </summary>
public class RootKeys : CubeKeys
{
    public const string Prefix = "rt";

    /// <summary>The application itself.</summary>
    public static Place Place { get; } = Place.Root;

    private const string ParentSlotName = $"{Prefix}-parent-slot";

    public RootKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected RootKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }

    /// <summary>Where the level below publishes its scope.</summary>
    public ScopeSlot ParentSlot
    {
        get => Intent.GetScopeSlot(ParentSlotName);
        set => Intent.SetScopeSlot(ParentSlotName, value);
    }
}
```

The list is a child of the root. It has no names of its own yet, but it declares its place:

<!-- snippet: list-keys -->
```csharp
/// <summary>The list: a child of the application. It has no names of its own; the contact's keys extend these.</summary>
public class ContactListKeys : RootKeys
{
    public new const string Prefix = "cl";

    public static new Place Place { get; } = RootKeys.Place.Child<ContactsApp>("contacts", app => new ContactListPresenter(app));

    public ContactListKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected ContactListKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }
}
```

`Place.Root` is the application itself. `contacts` is a child of the root, `contacts/contact` (declared by the
contact's keys, in step 5) a child of `contacts`. Going to `contacts/contact` keeps the list's presenter alive and
adds the contact's: a deeper place is drawn *inside* the places above it.

## 3. The application presenter

The application presenter is the root of the tree. It owns the page frame's state (its **scope**) and the services
the presenters below it use. A scope is the state a view draws: properties that notify changes with
`Set(ref field, value)`.

<!-- snippet: root-scope -->
```csharp
public sealed class RootScope : Scope
{
    /// <summary>What the place below the application shows.</summary>
    public Scope? Body { get; set => Set(ref field, value); }
}
```

Presenters do not reach into each other to show their screens. A level hands the level below a **slot**: a function
that receives the child's scope. The slot travels in the intent of the navigation, under the name `RootKeys` gives it
(`ParentSlot`).

The application lists its places, republishes its slot for the place below it, and starts from the address:

<!-- snippet: app -->
```csharp
/// <summary>The application presenter: the page frame, and the services the presenters below it use.</summary>
public sealed class ContactsApp : ApplicationPresenter<RootScope>
{
    /// <summary>Every place an address can name, each declared by its keys.</summary>
    public static IReadOnlyList<Place> AllPlaces { get; } = [ContactListKeys.Place, ContactKeys.Place];

    public ContactsApp(HistoryManager history, IContactBook book, PresentationDispatcher? dispatcher = null,
        ILogger? logger = null)
        : base(new RootScope(), history, AllPlaces, logger, dispatcher)
    {
        Book = book;
    }

    public IContactBook Book { get; }

    /// <summary>Starts from the address; an empty one shows the list.</summary>
    public Task Start() => InvokeAsync(async () =>
    {
        await KickStart(ContactListKeys.Place);
        if (!HasNavigated)
        {
            await new ContactListKeys(this).Flip();
        }
    });

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        if (last)
        {
            Scope.Body = null; // the application itself: nothing below it
        }
        else
        {
            new RootKeys(this, intent).ParentSlot = body => Scope.Body = body;
        }

        return Task.FromResult(true);
    }
}
```

- `ApplyParameters` runs on every navigation that passes through a level. `last` says whether the navigation stops
  here; when it does not, the level hands its slot down.
- `KickStart` reads the current address and navigates there. On a failure it goes to the safe place instead.

## 4. The list

Each place has a presenter and a scope. The list's scope has the rows, an action, and a slot of its own for the
contact shown beside it. Actions are plain delegates: the view calls them, the presenter binds them.

<!-- snippet: list-scope -->
```csharp
public sealed record ContactRow(long Id, string Name);

public sealed class ContactListScope : Scope
{
    public IReadOnlyList<ContactRow> Contacts { get; set => Set(ref field, value); } = [];

    /// <summary>The open contact (the place below the list), or null.</summary>
    public Scope? Detail { get; set => Set(ref field, value); }

    public Func<long, Task> OnOpen { get; set; } = ScopeActions.Unbound<long>();
}
```

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
            Scope.OnOpen = Action<long>(OnOpen);
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

    private Task OnOpen(long contactId) => new ContactKeys(App) { ContactId = contactId }.Flip();
}
```

- `keys.ParentSlot(Scope)` puts the list into the application's body.
- `initialization` is true only the first time the presenter is reached; that is where actions are bound and data is
  loaded. Later navigations through the list (say, from one contact to another) reuse it.
- `Action(...)` wraps a handler: it runs on the application's dispatcher, reports failures, and redraws what changed.
- Opening a contact is a navigation: fill the target's keys and flip. The presenter never touches the browser.

## 5. The contact

The contact's keys extend the list's, declare the place below it, and add a **parameter**, the contact's id.
Parameters go to the address; attributes (like the slots) travel only within the app.

<!-- snippet: contact-keys -->
```csharp
public sealed class ContactKeys : ContactListKeys
{
    public new const string Prefix = "ct";

    /// <summary>A child of the list: <c>contacts/contact?ct-id=2</c> is the detail of contact 2.</summary>
    public static new Place Place { get; } =
        ContactListKeys.Place.Child<ContactsApp>("contact", app => new ContactDetailPresenter(app));

    private const string ContactIdName = $"{Prefix}-id";

    public ContactKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }

    /// <summary>A parameter: it goes to the address. Garbage reads as none.</summary>
    public long? ContactId
    {
        get => Intent.GetInt64(ContactIdName) is > 0 and var id ? id : null;
        set => Intent.SetParameter(ContactIdName, value);
    }
}
```

<!-- snippet: detail-scope -->
```csharp
public sealed class ContactDetailScope : Scope
{
    public string Name { get; set => Set(ref field, value); } = string.Empty;

    public string Email { get; set => Set(ref field, value); } = string.Empty;

    public string Phone { get; set => Set(ref field, value); } = string.Empty;

    public Func<Task> OnClose { get; set; } = ScopeActions.Unbound();
}
```

<!-- snippet: detail-presenter -->
```csharp
public sealed class ContactDetailPresenter(ContactsApp app)
    : CubePresenter<ContactsApp, ContactDetailScope>(app, new ContactDetailScope())
{
    private long? _contactId;

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new ContactKeys(App, intent);
        var contact = keys.ContactId is { } id ? await App.Book.Find(id) : null;
        if (contact is null)
        {
            // Nothing to show: go to the list instead, within this same navigation.
            await App.FlipToIntent(intent.Redirect(ContactListKeys.Place));
            return false;
        }

        if (initialization)
        {
            Scope.OnClose = Action(OnClose);
        }

        _contactId = contact.Id;
        Scope.Name = contact.Name;
        Scope.Email = contact.Email;
        Scope.Phone = contact.Phone;
        keys.ParentSlot(Scope); // beside the list
        return true;
    }

    /// <summary>What this place puts in the address when the application publishes it.</summary>
    public override void PublishParameters(FlipIntent intent) => new ContactKeys(App, intent).ContactId = _contactId;

    private Task OnClose() => new ContactListKeys(App).Flip();
}
```

- The presenter reads the id from the address, so `contacts/contact?ct-id=3` typed by hand works like a click.
- Returning `false` after `intent.Redirect(...)` sends the same navigation somewhere else: an unknown id shows the
  list, and the address follows.
- `PublishParameters` writes what the place puts in the address. The application asks every presenter on the path
  after each navigation, so the address always reflects the live presenters.

## 6. The views

A view draws one scope and forwards events to the scope's actions. `ScopeView<TScope>` redraws when the scope
changes; `ViewSlot` draws whatever scope is in a slot, using the registry:

<!-- snippet: view-catalog -->
```csharp
/// <summary>Which component draws each scope that can land in a slot.</summary>
public static class ViewCatalog
{
    public static void Register(ViewRegistry views) => views
        .Register<RootScope, RootView>()
        .Register<ContactListScope, ContactListView>()
        .Register<ContactDetailScope, ContactDetailView>();
}
```

<!-- snippet: root-view -->
```razor
@inherits ScopeView<RootScope>

<header>Contacts</header>
<main>
    <ViewSlot Scope="Scope.Body" />
</main>
```

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

<!-- snippet: detail-view -->
```razor
@inherits ScopeView<ContactDetailScope>

<section class="contact">
    <h2>@Scope.Name</h2>
    <p>✉ @Scope.Email</p>
    <p>☎ @Scope.Phone</p>
    <button class="close" @onclick="Scope.OnClose">Close</button>
</section>
```

There is no logic here: no loading, no navigation, no address. Everything a view shows comes from its scope.

## 7. The host

The host registers the views and hands every address to one page, where `CubeHost` creates the application for the
circuit and starts it:

<!-- snippet: program -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<IContactBook, ContactBook>();
builder.Services.AddCubeViews(ViewCatalog.Register); // which component draws each scope

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
```

<!-- snippet: shell -->
```razor
@page "/"
@page "/{*path}"
@inject IContactBook Book

@* Every path is this page: the address belongs to the application, which turns it into a place. *@
<CubeHost TApp="ContactsApp" CreateApplication="host => new ContactsApp(host.History, Book, host.Dispatcher)"
          Start="app => app.Start()">
    <ViewSlot Scope="context.Scope" />
</CubeHost>

@code {
    [Parameter]
    public string? Path { get; set; }
}
```

## 8. Testing without a view

Because presenters and scopes are plain C#, the tests drive the app the way the views do: they call the scope's
actions and read the scopes. `TestHistoryManager` stands in for the browser's address bar, and `Settle()` waits for
the navigation and the pending updates to finish.

<!-- snippet: tests -->
```csharp
/// <summary>The whole application, driven as a view would drive it, with no view at all.</summary>
public sealed class ContactsTests
{
    private static async Task<(ContactsApp App, TestHistoryManager History)> Start(string address = "")
    {
        var history = new TestHistoryManager(address);
        var app = new ContactsApp(history, new ContactBook());
        await app.Start();
        await app.Settle();
        return (app, history);
    }

    private static ContactListScope List(ContactsApp app) => Assert.IsType<ContactListScope>(app.Scope.Body);

    [Fact]
    public async Task AnEmptyAddress_ShowsTheList()
    {
        var (app, history) = await Start();

        Assert.Equal(["Ada Lovelace", "Alan Turing", "Grace Hopper"], List(app).Contacts.Select(c => c.Name));
        Assert.Null(List(app).Detail);
        Assert.Equal("contacts", history.Token);
    }

    [Fact]
    public async Task OpeningAContact_ShowsItBesideTheList_AndNamesItInTheAddress()
    {
        var (app, history) = await Start();

        await List(app).OnOpen(2);
        await app.Settle();

        var detail = Assert.IsType<ContactDetailScope>(List(app).Detail);
        Assert.Equal("Alan Turing", detail.Name);
        Assert.Equal("contacts/contact?ct-id=2", history.Token);
    }

    [Fact]
    public async Task TheAddressAlone_RebuildsTheScreen()
    {
        var (app, _) = await Start("contacts/contact?ct-id=3");

        Assert.Equal("Grace Hopper", Assert.IsType<ContactDetailScope>(List(app).Detail).Name);
    }

    [Fact]
    public async Task AnUnknownContact_FallsBackToTheList()
    {
        var (app, history) = await Start("contacts/contact?ct-id=99");

        Assert.Null(List(app).Detail);
        Assert.Equal("contacts", history.Token);
    }

    [Fact]
    public async Task Closing_GoesBackToTheList()
    {
        var (app, history) = await Start("contacts/contact?ct-id=1");

        await Assert.IsType<ContactDetailScope>(List(app).Detail).OnClose();
        await app.Settle();

        Assert.Null(List(app).Detail);
        Assert.Equal("contacts", history.Token);
    }
}
```

```bash
dotnet test --project samples/GettingStarted/Contacts.Tests/Contacts.Tests.csproj
```

## Where to go next

- [Architecture](concepts/architecture.md): places, intents, presenters, scopes, slots and keys in depth.
- [Update cycle](concepts/update-cycle.md): how scope changes reach the screen.
- [Navigation](concepts/navigation.md): what a navigation guarantees when a presenter refuses or fails.
- [History](concepts/history.md): addresses, Back/Forward, sealed addresses.
- The [Shopping sample](https://github.com/mrcdom/wdc-cube-csharp/blob/main/samples/Shopping/README.md): a complete app with login, database, rules per role, and
  three hosts (Blazor Server, WebAssembly, Avalonia).
