using System.Text;
using Wdc.Cube.Testing;

namespace Wdc.Cube.Tests.Navigation;

/// <summary>The application presenter: kick-start from the address, its part in every walk, and the address codec.</summary>
public sealed class ApplicationPresenterTests
{
    [Fact]
    public async Task KickStart_ColdStartAtADeepAddress_RebuildsTheFace()
    {
        var app = new MainApp(new TestHistoryManager("orders/detail?mt=dark&oid=7"));

        await app.KickStart(MainPlaces.Home);

        Assert.Equal(("/", true, true), app.Applied[0]); // the application itself, once, as the destination
        Assert.Equal(("orders/detail", false, false), app.Applied[1]); // then as the root of the real walk
        Assert.Equal("dark", app.Theme);
        Assert.Same(MainPlaces.Detail, app.LastPlace);
        Assert.Equal(7, app.PresenterOf<OrdersPresenter>(MainPlaces.Orders).OrderId);
        Assert.Equal("orders/detail?mt=dark&oid=7", app.History.Token); // the application publishes first
        Assert.Same(app.PresenterOf<PagePresenter>(MainPlaces.Detail).Scope,
            app.PresenterOf<OrdersPresenter>(MainPlaces.Orders).Scope.Content);
    }

    [Fact]
    public async Task KickStart_EmptyAddress_StaysAtTheRoot()
    {
        var app = new MainApp(new TestHistoryManager());

        await app.KickStart(MainPlaces.Home);

        Assert.Single(app.Applied);
        Assert.Equal(0, app.PresenterCount);
        Assert.False(app.HasNavigated);
    }

    [Fact]
    public async Task KickStart_AddressThatFails_GoesToTheSafePlace()
    {
        var app = new MainApp(new TestHistoryManager("orders/detail?boom=true"));

        await app.KickStart(MainPlaces.Home);

        Assert.Same(MainPlaces.Home, app.LastPlace);
        Assert.Single(app.UnexpectedErrors);
        Assert.Null(app.GetPresenter(MainPlaces.Detail));
    }

    [Fact]
    public async Task ApplicationRefusing_StopsTheWalkAtTheRoot()
    {
        var app = new MainApp(new TestHistoryManager());
        await app.KickStart(MainPlaces.Home);
        await app.Flip(MainPlaces.Home);
        var home = app.PresenterOf<PagePresenter>(MainPlaces.Home);

        app.RefuseAll = true;
        var reached = await app.Flip(MainPlaces.Orders);

        Assert.False(reached);
        Assert.Same(Place.Root, app.LastPlace); // accepted, and stopped at the application itself
        Assert.Equal(0, app.PresenterCount);
        Assert.True(home.IsReleased);
    }

    [Fact]
    public async Task ApplicationActions_AreGuardedToo()
    {
        var app = new MainApp(new TestHistoryManager());
        await app.KickStart(MainPlaces.Home);

        await app.Scope.OnToggleTheme();

        Assert.Equal("dark", app.Theme);
        Assert.Equal("mt=dark", app.History.Token.Split('?').Last());
    }

    // :: Codec

    [Fact]
    public async Task Codec_TheAddressTravelsSealed_ButReadsPlain()
    {
        var history = new TestHistoryManager { Codec = new Base64Codec() };
        var app = new MainApp(history);
        await app.KickStart(MainPlaces.Home);

        await app.Flip(MainPlaces.Orders, i => i.SetParameter("oid", 7L));

        Assert.Equal("orders?oid=7", history.Token);
        Assert.StartsWith("orders?x=", history.EncodedToken, StringComparison.Ordinal);
        Assert.DoesNotContain("oid", history.EncodedToken, StringComparison.Ordinal);
        Assert.Equal("orders?oid=7", history.Location);
    }

    [Fact]
    public async Task Codec_ColdStartFromASealedAddress()
    {
        var codec = new Base64Codec();
        var history = new TestHistoryManager("orders?x=" + Uri.EscapeDataString(codec.Encode("mt=dark&oid=9")))
        {
            Codec = codec, // installed before KickStart, which reads the address first
        };
        var app = new MainApp(history);

        await app.KickStart(MainPlaces.Home);

        Assert.Equal("dark", app.Theme);
        Assert.Equal(9, app.PresenterOf<OrdersPresenter>(MainPlaces.Orders).OrderId);
    }

    [Fact]
    public async Task Codec_AnOldPlainBookmarkStillOpens()
    {
        var app = new MainApp(new TestHistoryManager("orders?mt=light") { Codec = new Base64Codec() });

        await app.KickStart(MainPlaces.Home);

        Assert.Equal("light", app.Theme);
        Assert.Same(MainPlaces.Orders, app.LastPlace);
    }

    [Fact]
    public async Task Codec_AnUnreadableEnvelopeOpensThePlaceInItsDefaultState()
    {
        var app = new MainApp(new TestHistoryManager("orders?x=not-base64!!") { Codec = new Base64Codec() });

        await app.KickStart(MainPlaces.Home);

        Assert.Same(MainPlaces.Orders, app.LastPlace);
        Assert.Null(app.Theme);
        Assert.Empty(app.UnexpectedErrors);
    }

    [Fact]
    public async Task Codec_NonDeterministic_DoesNotRepublishTheSameState()
    {
        var history = new TestHistoryManager { Codec = new NonceCodec() };
        var app = new MainApp(history);
        await app.KickStart(MainPlaces.Home);
        await app.Flip(MainPlaces.Orders, i => i.SetParameter("oid", 1L));
        var published = history.Tokens.Count;

        await app.InvokeAsync(app.UpdateHistory);
        await app.InvokeAsync(app.UpdateHistory);

        Assert.Equal(published, history.Tokens.Count); // plain compared to plain: no loop, no duplicates
    }

    [Fact]
    public async Task Codec_SwappedAtRunTime_RepublishesTheForm()
    {
        var history = new TestHistoryManager();
        var app = new MainApp(history);
        await app.KickStart(MainPlaces.Home);
        await app.Flip(MainPlaces.Orders, i => i.SetParameter("oid", 1L));
        Assert.Equal("orders?oid=1", history.EncodedToken);

        history.Codec = new Base64Codec();
        await app.InvokeAsync(app.UpdateHistory);

        Assert.Equal("orders?oid=1", history.Token); // same state...
        Assert.StartsWith("orders?x=", history.EncodedToken, StringComparison.Ordinal); // ...new form
    }
}

// :: Fixture

internal static class MainPlaces
{
    public static readonly Place Home = Place.Create<MainApp>("home", a => new PagePresenter(a));
    public static readonly Place Orders = Place.Create<MainApp>("orders", a => new OrdersPresenter(a));
    public static readonly Place Detail = Orders.Child<MainApp>("detail", a => new PagePresenter(a));
    public static readonly IReadOnlyList<Place> All = [Home, Orders, Detail];
}

internal sealed class MainScope : Scope
{
    public Scope? Body { get; set => Set(ref field, value); }

    public Func<Task> OnToggleTheme { get; set; } = ScopeActions.Unbound();
}

internal sealed class MainApp : ApplicationPresenter<MainScope>
{
    public MainApp(TestHistoryManager history)
        : base(new MainScope(), history, MainPlaces.All)
    {
        Scope.OnToggleTheme = Action(() => Theme = Theme == "dark" ? null : "dark");
    }

    public new TestHistoryManager History => (TestHistoryManager)base.History;

    public List<(string Path, bool Initialization, bool Last)> Applied { get; } = [];

    public List<Exception> UnexpectedErrors { get; } = [];

    public string? Theme { get; private set; }

    public bool RefuseAll { get; set; }

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        Applied.Add((intent.Place.PathName, initialization, last));
        if (RefuseAll)
        {
            return Task.FromResult(false);
        }

        Theme = intent.GetString("mt") ?? Theme;
        if (last)
        {
            Scope.Body = null;
        }
        else
        {
            intent.SetScopeSlot("parent", body => Scope.Body = body);
        }

        return Task.FromResult(true);
    }

    public override void PublishParameters(FlipIntent intent) => intent.SetParameter("mt", Theme);

    public override void Unexpected(string message, Exception error) => UnexpectedErrors.Add(error);
}

internal sealed class PageScope : Scope;

internal sealed class PagePresenter(MainApp app) : CubePresenter<MainApp, PageScope>(app, new PageScope())
{
    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        if (intent.GetBoolean("boom") == true)
        {
            throw new InvalidOperationException("boom");
        }

        intent.GetScopeSlot("parent")(Scope);
        return Task.FromResult(true);
    }
}

internal sealed class OrdersScope : Scope
{
    public Scope? Content { get; set => Set(ref field, value); }
}

internal sealed class OrdersPresenter(MainApp app) : CubePresenter<MainApp, OrdersScope>(app, new OrdersScope())
{
    public long? OrderId { get; private set; }

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        OrderId = intent.GetInt64("oid") ?? OrderId;
        var parentSlot = intent.GetScopeSlot("parent");
        if (last)
        {
            Scope.Content = null;
        }
        else
        {
            intent.SetScopeSlot("parent", content => Scope.Content = content); // same name: visual parenthood moves down
        }

        parentSlot(Scope);
        return Task.FromResult(true);
    }

    public override void PublishParameters(FlipIntent intent) => intent.SetParameter("oid", OrderId);
}

/// <summary>Reversible and deterministic; stands in for a real cipher.</summary>
internal sealed class Base64Codec : IHistoryCodec
{
    public string Envelope => "x";

    public string Encode(string queryString) => Convert.ToBase64String(Encoding.UTF8.GetBytes(queryString));

    public string? Decode(string payload)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Seals the same state differently every time, like an AEAD with a random nonce.</summary>
internal sealed class NonceCodec : IHistoryCodec
{
    private readonly Base64Codec _inner = new();

    public string Envelope => "x";

    public string Encode(string queryString) => Guid.NewGuid().ToString("N")[..8] + _inner.Encode(queryString);

    public string? Decode(string payload) => payload.Length < 8 ? null : _inner.Decode(payload[8..]);
}
