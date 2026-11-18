using Wdc.Cube.Testing;

namespace Wdc.Cube.Tests.Navigation;

/// <summary>
/// The navigation guarantees, ported from the 14 scenarios of <c>CubeNavigationTest.kt</c> (Kotlin).
/// </summary>
/// <remarks>
/// The Kotlin tests swapped a place's factory per scenario; here the tree is fixed and each test tells
/// <see cref="NavApp"/> how the presenter born at a place behaves. Kotlin's explicit step lists
/// (<c>ROOT, PRODUCTS</c>) become the path of a place (<c>root/products</c>).
/// Extra assertions cover decision D-02: <c>false</c> commits up to the presenter that returned it, and every
/// presenter is released exactly once.
/// </remarks>
public sealed class NavigationGuaranteesTests
{
    private readonly NavApp _app = new();

    private TestPresenter? PresenterOf(Place place) => _app.GetPresenter<TestPresenter>(place);

    private void AssertEveryPresenterReleasedAtMostOnce() =>
        Assert.All(_app.Created, p => Assert.InRange(p.ReleaseCount, 0, 1));

    [Fact]
    public async Task Scenario01_NormalNavigation_CommitsNewAndReusedPresenters()
    {
        Assert.True(await _app.Flip(NavPlaces.Home));
        var root = PresenterOf(NavPlaces.Root)!;
        var home = PresenterOf(NavPlaces.Home)!;
        Assert.True(root.Initialized && home.Initialized);
        Assert.False(root.Released || home.Released);

        Assert.True(await _app.Flip(NavPlaces.Products));
        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.True(home.Released);
        Assert.False(PresenterOf(NavPlaces.Products)!.Released);
        Assert.Null(PresenterOf(NavPlaces.Home));
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario02_ExceptionInApplyParameters_RollsBack()
    {
        await _app.Flip(NavPlaces.Home);
        var root = PresenterOf(NavPlaces.Root)!;
        var home = PresenterOf(NavPlaces.Home)!;

        var error = new InvalidOperationException("test error");
        _app.Behave(NavPlaces.Products, new() { Throw = error });

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => _app.Flip(NavPlaces.Products));
        Assert.Same(error, caught);

        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.Same(home, PresenterOf(NavPlaces.Home));
        Assert.False(root.Released || home.Released);
        Assert.True(_app.CreatedAt(NavPlaces.Products).Single().Released);
        Assert.Same(NavPlaces.Home, _app.LastPlace);
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario03_RedirectInterrupts_AndTheRedirectCommits()
    {
        await _app.Flip(NavPlaces.Home);
        var root = PresenterOf(NavPlaces.Root)!;
        var home = PresenterOf(NavPlaces.Home)!;
        _app.Behave(NavPlaces.Products, new() { RedirectTo = NavPlaces.Cart });

        var reachedTarget = await _app.Flip(NavPlaces.ProductDetail);

        Assert.False(reachedTarget);
        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.False(PresenterOf(NavPlaces.Cart)!.Released);
        Assert.True(home.Released);
        Assert.Null(PresenterOf(NavPlaces.Home));
        Assert.True(_app.CreatedAt(NavPlaces.Products).Single().Released); // orphan of the interrupted walk
        Assert.Empty(_app.CreatedAt(NavPlaces.ProductDetail));
        Assert.Same(NavPlaces.Cart, _app.LastPlace);
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario04_RedirectThatFails_RollsBackAndReleasesEverythingCreated()
    {
        await _app.Flip(NavPlaces.Home);
        var root = PresenterOf(NavPlaces.Root)!;
        var home = PresenterOf(NavPlaces.Home)!;
        _app.Behave(NavPlaces.Products, new() { RedirectTo = NavPlaces.Cart });
        _app.Behave(NavPlaces.Cart, new() { Throw = new InvalidOperationException("cart error") });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _app.Flip(NavPlaces.Products));

        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.Same(home, PresenterOf(NavPlaces.Home));
        Assert.False(root.Released || home.Released);

        var created = _app.CreatedAt(NavPlaces.Products).Concat(_app.CreatedAt(NavPlaces.Cart)).ToList();
        Assert.Equal(2, created.Count);
        Assert.All(created, p => Assert.Equal(1, p.ReleaseCount));
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario05_OrphanedPresenters_AreReleasedOnCommit()
    {
        await _app.Flip(NavPlaces.Root);
        var root = PresenterOf(NavPlaces.Root)!;
        _app.Behave(NavPlaces.Products, new() { RedirectTo = NavPlaces.Home });

        await _app.Flip(NavPlaces.Products);

        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.NotNull(PresenterOf(NavPlaces.Home));
        Assert.Null(PresenterOf(NavPlaces.Products));
        Assert.Equal(1, _app.CreatedAt(NavPlaces.Products).Single().ReleaseCount);
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario06_ChainedRedirects_ReleaseEveryOrphan()
    {
        await _app.Flip(NavPlaces.Root);
        var root = PresenterOf(NavPlaces.Root)!;
        _app.Behave(NavPlaces.Products, new() { RedirectTo = NavPlaces.Home });
        _app.Behave(NavPlaces.Home, new() { RedirectTo = NavPlaces.Cart });

        await _app.Flip(NavPlaces.Products);

        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.False(PresenterOf(NavPlaces.Cart)!.Released);
        Assert.True(_app.CreatedAt(NavPlaces.Products).Single().Released);
        Assert.True(_app.CreatedAt(NavPlaces.Home).Single().Released);
        Assert.Same(NavPlaces.Cart, _app.LastPlace);
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario07_FalseStopsTheComposition_AndCommitsUpToThatPresenter()
    {
        await _app.Flip(NavPlaces.Home);
        var root = PresenterOf(NavPlaces.Root)!;
        var home = PresenterOf(NavPlaces.Home)!;
        _app.Behave(NavPlaces.Products, new() { GoAhead = false });

        var reachedTarget = await _app.Flip(NavPlaces.ProductDetail);

        Assert.False(reachedTarget);
        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.NotNull(PresenterOf(NavPlaces.Products));
        Assert.True(home.Released);
        Assert.Null(PresenterOf(NavPlaces.ProductDetail));
        Assert.Empty(_app.CreatedAt(NavPlaces.ProductDetail));

        // D-02: the current place is the last accepted presenter, not the leaf the intent asked for.
        Assert.Same(NavPlaces.Products, _app.LastPlace);
        Assert.Equal("root/products", _app.History.Token);
    }

    [Fact]
    public async Task Scenario08_RedirectReusesWhatTheInterruptedWalkCreated()
    {
        await _app.Flip(NavPlaces.Root);
        _app.Behave(NavPlaces.ProductDetail, new() { RedirectTo = NavPlaces.ProductsCart });

        await _app.Flip(NavPlaces.ProductDetail);

        Assert.Single(_app.CreatedAt(NavPlaces.Products)); // created once, reused by the redirect
        Assert.NotNull(PresenterOf(NavPlaces.Root));
        Assert.False(PresenterOf(NavPlaces.Products)!.Released);
        Assert.NotNull(PresenterOf(NavPlaces.ProductsCart));
        Assert.True(_app.CreatedAt(NavPlaces.ProductDetail).Single().Released);
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario09_ReleaseOrder_IsLeafFirst()
    {
        await _app.Flip(NavPlaces.Products);
        await _app.Flip(NavPlaces.ProductDetail);
        _app.ReleaseLog.Clear();

        await _app.Flip(NavPlaces.Settings);

        Assert.Equal([NavPlaces.ProductDetail, NavPlaces.Products, NavPlaces.Root], _app.ReleaseLog);
        Assert.NotNull(PresenterOf(NavPlaces.Settings));
        Assert.Equal(1, _app.PresenterCount);
    }

    [Fact]
    public async Task Scenario10_SequentialNavigations()
    {
        await _app.Flip(NavPlaces.Home);
        var root = PresenterOf(NavPlaces.Root)!;
        var home = PresenterOf(NavPlaces.Home)!;

        await _app.Flip(NavPlaces.Products);
        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.True(home.Released);
        var products = PresenterOf(NavPlaces.Products)!;

        await _app.Flip(NavPlaces.ProductDetail);
        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.Same(products, PresenterOf(NavPlaces.Products));
        Assert.False(products.Released);
        var detail = PresenterOf(NavPlaces.ProductDetail)!;

        await _app.Flip(NavPlaces.Root);
        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.True(products.Released && detail.Released);
        Assert.Equal(1, _app.PresenterCount);
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario11_Rollback_ReappliesTheSourceIntentToTheOriginalPresenters()
    {
        await _app.Flip(NavPlaces.Home);
        var root = PresenterOf(NavPlaces.Root)!;
        var home = PresenterOf(NavPlaces.Home)!;
        root.Applied.Clear();
        home.Applied.Clear();
        _app.Behave(NavPlaces.Products, new() { Throw = new InvalidOperationException("fail") });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _app.Flip(NavPlaces.Products));

        // ROOT: once by the failed walk, once by the rollback. HOME: only by the rollback, as the leaf.
        Assert.Equal(2, root.Applied.Count);
        Assert.Equal(("root/home", false, false), root.Applied[^1]);
        Assert.Equal([("root/home", false, true)], home.Applied);
        Assert.False(root.Released || home.Released);
    }

    [Fact]
    public async Task Scenario12_EndlessRedirects_AreStopped()
    {
        await _app.Flip(NavPlaces.Root);
        var root = PresenterOf(NavPlaces.Root)!;
        _app.Behave(NavPlaces.Home, new() { RedirectTo = NavPlaces.Home });

        await Assert.ThrowsAsync<CubeNavigationException>(() => _app.Flip(NavPlaces.Home));

        Assert.Same(root, PresenterOf(NavPlaces.Root));
        Assert.Null(PresenterOf(NavPlaces.Home));
        Assert.True(_app.CreatedAt(NavPlaces.Home).Single().Released);
        AssertEveryPresenterReleasedAtMostOnce();
    }

    [Fact]
    public async Task Scenario13_FirstNavigationOnAnEmptyApplication()
    {
        Assert.Equal(0, _app.PresenterCount);
        Assert.False(_app.HasNavigated);
        Assert.Same(Place.Root, _app.LastPlace);

        await _app.Flip(NavPlaces.Home);

        Assert.Equal(2, _app.PresenterCount);
        Assert.True(PresenterOf(NavPlaces.Root)!.Initialized);
        Assert.True(PresenterOf(NavPlaces.Home)!.Initialized);
        Assert.True(_app.HasNavigated);
    }

    [Fact]
    public async Task Scenario14_HistoryIsUpdatedAfterCommitAndAfterRollback()
    {
        var before = _app.History.UpdateCount;
        await _app.Flip(NavPlaces.Root);
        Assert.True(_app.History.UpdateCount > before);

        var beforeRollback = _app.History.UpdateCount;
        _app.Behave(NavPlaces.Home, new() { Throw = new InvalidOperationException("fail") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _app.Flip(NavPlaces.Home));
        Assert.True(_app.History.UpdateCount > beforeRollback);
        Assert.Equal("root", _app.History.Token);
    }

    [Fact]
    public async Task Release_ReleasesEveryLivePresenterLeafFirst_Once()
    {
        await _app.Flip(NavPlaces.ProductDetail);
        _app.ReleaseLog.Clear();

        _app.Release();
        _app.Release();

        Assert.Equal([NavPlaces.ProductDetail, NavPlaces.Products, NavPlaces.Root], _app.ReleaseLog);
        Assert.Equal(0, _app.PresenterCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _app.Flip(NavPlaces.Root));
        AssertEveryPresenterReleasedAtMostOnce();
    }
}

// :: Fixture

internal static class NavPlaces
{
    public static readonly Place Root = Place.Create<NavApp>("root", a => new TestPresenter(a, Root!));
    public static readonly Place Home = Root.Child<NavApp>("home", a => new TestPresenter(a, Home!));
    public static readonly Place Products = Root.Child<NavApp>("products", a => new TestPresenter(a, Products!));
    public static readonly Place ProductDetail = Products.Child<NavApp>("detail", a => new TestPresenter(a, ProductDetail!));
    public static readonly Place ProductsCart = Products.Child<NavApp>("cart", a => new TestPresenter(a, ProductsCart!));
    public static readonly Place Cart = Root.Child<NavApp>("cart", a => new TestPresenter(a, Cart!));
    public static readonly Place Settings = Place.Create<NavApp>("settings", a => new TestPresenter(a, Settings!));

    public static readonly IReadOnlyList<Place> All = [Root, Home, Products, ProductDetail, ProductsCart, Cart, Settings];
}

/// <summary>How the presenter born at a place behaves.</summary>
internal sealed record PlaceBehavior
{
    public Exception? Throw { get; init; }
    public Place? RedirectTo { get; init; }
    public bool GoAhead { get; init; } = true;
}

internal sealed class NavApp() : CubeApplication(new TestHistoryManager(), NavPlaces.All)
{
    private readonly Dictionary<Place, PlaceBehavior> _behaviors = [];

    public new TestHistoryManager History => (TestHistoryManager)base.History;

    public List<TestPresenter> Created { get; } = [];

    public List<Place> ReleaseLog { get; } = [];

    public void Behave(Place place, PlaceBehavior behavior) => _behaviors[place] = behavior;

    public PlaceBehavior BehaviorOf(Place place) => _behaviors.GetValueOrDefault(place) ?? new PlaceBehavior();

    public IEnumerable<TestPresenter> CreatedAt(Place place) => Created.Where(p => p.Place == place);
}

internal sealed class TestPresenter : ICubePresenter
{
    private readonly NavApp _app;

    public TestPresenter(NavApp app, Place place)
    {
        _app = app;
        Place = place;
        app.Created.Add(this);
    }

    public Place Place { get; }

    public bool Initialized { get; private set; }

    public int ReleaseCount { get; private set; }

    public bool Released => ReleaseCount > 0;

    /// <summary>Every call: (intent path, initialization, last).</summary>
    public List<(string Path, bool Initialization, bool Last)> Applied { get; } = [];

    public async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        Applied.Add((intent.Place.PathName, initialization, last));
        Initialized |= initialization;

        var behavior = _app.BehaviorOf(Place);
        if (behavior.Throw is { } error)
        {
            throw error;
        }

        if (behavior.RedirectTo is { } target)
        {
            await _app.Flip(target);
            return false;
        }

        await Task.Yield(); // a real presenter awaits something; make sure the algorithm survives it
        return behavior.GoAhead;
    }

    public void PublishParameters(FlipIntent intent)
    {
    }

    public void Release()
    {
        ReleaseCount++;
        _app.ReleaseLog.Add(Place);
    }
}
