using Wdc.Cube.Testing;

namespace Wdc.Cube.Tests.Navigation;

/// <summary>
/// Use-case scenarios ported from <c>Cube.test.ts</c> (TypeScript): a session guard that redirects to login,
/// composition through scope slots, the address published from the live presenters, and navigation from an address.
/// </summary>
/// <remarks>
/// Differences from the TS original: the address carries the full path (<c>root/restricted</c>), and the parts that
/// depend on the update pipeline (computed value in <c>onBeforeScopeUpdate</c>) wait for it.
/// </remarks>
public sealed class ApplicationScenarioTests
{
    [Fact]
    public async Task BasicNavigation_LoginGuardAndSlots()
    {
        var app = new ShopApp();
        await app.Flip(ShopPlaces.Login);

        var root = app.GetPresenter<RootPresenter>(ShopPlaces.Root)!;
        Assert.True(root.Initialized);
        Assert.False(root.Deepest);
        Assert.Equal(0, app.Session.Id);

        var login = app.GetPresenter<LoginPresenter>(ShopPlaces.Login)!;
        Assert.True(login.Initialized);
        Assert.True(login.Deepest);
        Assert.Null(login.Scope.UserName);
        Assert.Same(login.Scope, root.Scope.Body);

        // Wrong password
        login.Scope.UserName = "test";
        login.Scope.Password = "wrong-password";
        await login.OnEnter();
        Assert.Equal("User or password invalid", login.Scope.Message);
        Assert.Same(login.Scope, root.Scope.Body);
        Assert.Equal(0, app.Session.Id);

        // The guard refuses the restricted area without a session and sends to login
        await app.Flip(ShopPlaces.Restricted);
        Assert.Null(app.GetPresenter(ShopPlaces.Restricted));
        Assert.Same(login, app.GetPresenter(ShopPlaces.Login)); // same instance
        Assert.True(login.Initialized); // still initialized
        Assert.Same(login.Scope, root.Scope.Body); // and still bound to the parent scope

        // A valid login
        login.Scope.Password = "test";
        await login.OnEnter();
        Assert.False(login.Initialized); // released
        Assert.Equal(1, app.Session.Id);
        var restricted = app.GetPresenter<RestrictedPresenter>(ShopPlaces.Restricted)!;
        Assert.Same(restricted.Scope, root.Scope.Body);

        // Logout
        await restricted.OnLogout();
        Assert.Equal(0, app.Session.Id);
        Assert.False(restricted.Initialized);
        Assert.True(app.GetPresenter<LoginPresenter>(ShopPlaces.Login)!.Initialized);
    }

    [Fact]
    public async Task TokenAccuracy_TheAddressIsTheSumOfWhatThePresentersPublish()
    {
        var app = new ShopApp();
        app.Session = new Session(1, true);

        await app.Flip(ShopPlaces.Restricted);
        var restricted = app.GetPresenter<RestrictedPresenter>(ShopPlaces.Restricted)!;
        Assert.True(restricted.Initialized);
        Assert.Equal("root/restricted?s=1", app.History.Token);

        await app.Flip(ShopPlaces.Product, i => i.SetParameter(ShopParams.ProductId, 9999L));
        Assert.True(restricted.Initialized);
        var product = app.GetPresenter<ProductPresenter>(ShopPlaces.Product)!;
        Assert.True(product.Initialized);
        Assert.Equal(9999, product.ProductId);
        Assert.Equal("product-9999", product.Scope.Name);
        Assert.Equal("root/restricted/product?s=1&p=9999", app.History.Token);

        await app.Flip(ShopPlaces.Cart, i => i.SetParameter(ShopParams.CartId, 1234L));
        Assert.True(restricted.Initialized);
        Assert.False(product.Initialized);
        var cart = app.GetPresenter<CartPresenter>(ShopPlaces.Cart)!;
        Assert.True(cart.Initialized);
        Assert.Equal(1234, cart.CartId);
        // The product left the face, so its parameter left the address: nothing to clean up by hand.
        Assert.Equal("root/restricted/cart?s=1&c=1234", app.History.Token);

        await app.Flip(ShopPlaces.Restricted);
        Assert.True(restricted.Initialized);
        Assert.False(cart.Initialized);
        Assert.Equal("root/restricted?s=1", app.History.Token);
    }

    [Fact]
    public async Task TokenNavigation_TheAddressRebuildsTheState()
    {
        var app = new ShopApp();
        app.Session = new Session(1, true);

        await app.FlipToIntentString("root/restricted/cart?s=1&c=1234");
        var root = app.GetPresenter<RootPresenter>(ShopPlaces.Root)!;
        Assert.True(root.Initialized);
        var cart = app.GetPresenter<CartPresenter>(ShopPlaces.Cart)!;
        Assert.True(cart.Initialized && cart.Deepest);
        Assert.Equal(1234, cart.CartId);
        Assert.Equal("root/restricted/cart?s=1&c=1234", app.History.Token);

        await app.FlipToIntentString("root/restricted/product?s=1&p=9999");
        var product = app.GetPresenter<ProductPresenter>(ShopPlaces.Product)!;
        Assert.True(product.Initialized && product.Deepest);
        Assert.Equal("product-9999", product.Scope.Name);
        Assert.Equal("root/restricted/product?s=1&p=9999", app.History.Token);

        await app.FlipToIntentString("root/restricted");
        var restricted = app.GetPresenter<RestrictedPresenter>(ShopPlaces.Restricted)!;
        Assert.True(restricted.Initialized && restricted.Deepest);
        Assert.Equal("root/restricted?s=1", app.History.Token);

        Assert.True(root.Initialized);
        Assert.False(cart.Initialized);
        Assert.False(product.Initialized);
    }

    [Fact]
    public async Task BackAndForward_FollowTheHistory()
    {
        var app = new ShopApp();
        app.Session = new Session(1, true);
        await app.Flip(ShopPlaces.Cart, i => i.SetParameter(ShopParams.CartId, 1L));

        app.History.Navigate("root/restricted/product?s=1&p=7");
        await WaitUntil(() => app.GetPresenter(ShopPlaces.Product) is not null);

        Assert.Equal(7, app.GetPresenter<ProductPresenter>(ShopPlaces.Product)!.ProductId);
        Assert.Null(app.GetPresenter(ShopPlaces.Cart));
    }

    [Fact]
    public async Task HasNavigated_SeparatesTheRootPlaceFromNowhereYet()
    {
        var app = new ShopApp();
        app.Session = new Session(1, true);

        Assert.False(app.HasNavigated);
        Assert.Same(app.RootPlace, app.LastPlace);

        await app.Flip(ShopPlaces.Restricted);

        Assert.True(app.HasNavigated);
        Assert.Same(ShopPlaces.Restricted, app.LastPlace);
    }

    [Fact]
    public async Task HasNavigated_StaysFalseWhileTheFirstFlipIsRunning()
    {
        var app = new ShopApp();
        app.Session = new Session(1, true);

        await app.Flip(ShopPlaces.Restricted);

        var seen = app.GetPresenter<RestrictedPresenter>(ShopPlaces.Restricted)!.SeenHasNavigated;
        Assert.NotEmpty(seen);
        Assert.All(seen, Assert.False);
        Assert.True(app.HasNavigated);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition(), "Condition not reached in time.");
    }
}

// :: Fixture (mirrors Cube.test.ts)

internal static class ShopParams
{
    public const string SessionId = "s";
    public const string CartId = "c";
    public const string ProductId = "p";
}

internal static class ShopAttrs
{
    public const string Parent = "parent";
}

internal static class ShopPlaces
{
    public static readonly Place Root = Place.Create<ShopApp>("root", a => new RootPresenter(a));
    public static readonly Place Login = Root.Child<ShopApp>("login", a => new LoginPresenter(a));
    public static readonly Place Restricted = Root.Child<ShopApp>("restricted", a => new RestrictedPresenter(a));
    public static readonly Place Cart = Restricted.Child<ShopApp>("cart", a => new CartPresenter(a));
    public static readonly Place Product = Restricted.Child<ShopApp>("product", a => new ProductPresenter(a));

    public static readonly IReadOnlyList<Place> All = [Root, Login, Restricted, Cart, Product];
}

internal sealed record Session(long Id, bool Active);

internal sealed class ShopApp() : CubeApplication(new TestHistoryManager(), ShopPlaces.All, ShopPlaces.Root)
{
    public new TestHistoryManager History => (TestHistoryManager)base.History;

    public Session Session { get; set; } = new(0, false);
}

internal static class EchoService
{
    public static async Task<T> Echo<T>(T value)
    {
        await Task.Yield();
        return value;
    }
}

internal abstract class ShopPresenter<TScope>(ShopApp app, TScope scope) : ICubePresenter
    where TScope : Scope
{
    protected ShopApp App { get; } = app;

    public TScope Scope { get; } = scope;

    public bool Initialized { get; protected set; }

    public bool Deepest { get; protected set; }

    public abstract Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last);

    public virtual void PublishParameters(FlipIntent intent)
    {
    }

    public virtual void Release() => Initialized = false;
}

internal sealed class RootScope : Scope
{
    public Scope? Body { get; set; }
}

internal sealed class RootPresenter(ShopApp app) : ShopPresenter<RootScope>(app, new RootScope())
{
    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var sessionId = intent.GetInt64(ShopParams.SessionId, App.Session.Id);
        if (initialization)
        {
            Initialized = true;
            App.Session = await EchoService.Echo(new Session(sessionId, true));
        }

        Deepest = last;
        if (last)
        {
            Scope.Body = null;
        }
        else
        {
            intent.SetScopeSlot(ShopAttrs.Parent, body => Scope.Body = body);
        }

        return true;
    }

    public override void PublishParameters(FlipIntent intent) => intent.SetParameter(ShopParams.SessionId, App.Session.Id);
}

internal sealed class LoginScope : Scope
{
    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string? Message { get; set; }
}

internal sealed class LoginPresenter(ShopApp app) : ShopPresenter<LoginScope>(app, new LoginScope())
{
    private ScopeSlot _parentSlot = ScopeSlots.Noop;

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        Initialized |= initialization;
        _parentSlot = intent.GetScopeSlot(ShopAttrs.Parent);
        Deepest = last;
        _parentSlot(Scope);
        return Task.FromResult(true);
    }

    public async Task OnEnter()
    {
        Scope.Message = null;
        if (Scope is { UserName: "test", Password: "test" })
        {
            App.Session = await EchoService.Echo(new Session(1, true));
            await App.Flip(ShopPlaces.Restricted);
        }
        else
        {
            Scope.Message = "User or password invalid";
        }
    }
}

internal sealed class RestrictedScope : Scope
{
    public Scope? Content { get; set; }
}

internal sealed class RestrictedPresenter(ShopApp app) : ShopPresenter<RestrictedScope>(app, new RestrictedScope())
{
    private ScopeSlot _parentSlot = ScopeSlots.Noop;

    public List<bool> SeenHasNavigated { get; } = [];

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        SeenHasNavigated.Add(App.HasNavigated);

        // Guard: no restricted area without a session
        if (App.Session.Id == 0)
        {
            await App.Flip(ShopPlaces.Login);
            return false;
        }

        Initialized |= initialization;
        _parentSlot = intent.GetScopeSlot(ShopAttrs.Parent);
        Deepest = last;
        if (last)
        {
            Scope.Content = null;
        }
        else
        {
            intent.SetScopeSlot(ShopAttrs.Parent, content => Scope.Content = content);
        }

        _parentSlot(Scope);
        return true;
    }

    public async Task OnLogout()
    {
        App.Session = new Session(0, false);
        await App.Flip(ShopPlaces.Login);
    }
}

internal sealed class CartScope : Scope;

internal sealed class CartPresenter(ShopApp app) : ShopPresenter<CartScope>(app, new CartScope())
{
    private ScopeSlot _parentSlot = ScopeSlots.Noop;

    public long? CartId { get; private set; }

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var cartId = intent.GetInt64(ShopParams.CartId);
        if (initialization)
        {
            Initialized = true;
            await LoadData(cartId);
        }
        else if (cartId is not null && cartId != CartId)
        {
            await LoadData(cartId);
        }

        _parentSlot = intent.GetScopeSlot(ShopAttrs.Parent);
        Deepest = last;
        _parentSlot(Scope);
        return true;
    }

    public override void PublishParameters(FlipIntent intent) => intent.SetParameter(ShopParams.CartId, CartId);

    private async Task LoadData(long? cartId) =>
        CartId = await EchoService.Echo(cartId ?? throw new InvalidOperationException("Missing cartId"));
}

internal sealed class ProductScope : Scope
{
    public string? Name { get; set; }
}

internal sealed class ProductPresenter(ShopApp app) : ShopPresenter<ProductScope>(app, new ProductScope())
{
    private ScopeSlot _parentSlot = ScopeSlots.Noop;

    public long? ProductId { get; private set; }

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var productId = intent.GetInt64(ShopParams.ProductId) ?? ProductId;
        if (initialization)
        {
            Initialized = true;
            await LoadData(productId);
        }
        else if (productId is not null && productId != ProductId)
        {
            await LoadData(productId);
        }

        _parentSlot = intent.GetScopeSlot(ShopAttrs.Parent);
        Deepest = last;
        _parentSlot(Scope);
        return true;
    }

    public override void PublishParameters(FlipIntent intent) => intent.SetParameter(ShopParams.ProductId, ProductId);

    private async Task LoadData(long? productId)
    {
        var id = await EchoService.Echo(productId ?? throw new InvalidOperationException("Missing productId"));
        ProductId = id;
        Scope.Name = $"product-{id}";
    }
}
