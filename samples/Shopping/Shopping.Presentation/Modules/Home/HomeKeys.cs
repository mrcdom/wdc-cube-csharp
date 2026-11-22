using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home;

/// <summary>Names of the restricted area. Deeper places extend these keys to read what Home hands down.</summary>
public class HomeKeys : RootKeys
{
    public new const string Prefix = "hm";

    /// <summary>The session guard and the shell (header, cart badge) of everything below it.</summary>
    public static new Place Place { get; } = RootKeys.Place.Child<RootPresenter>("home", app => new HomePresenter(app));

    private static class P
    {
        /// <summary>The page of the purchase history (1-based; the first stays out of the address).</summary>
        public const string Page = $"{Prefix}-page";
    }

    private static class A
    {
        /// <summary>The Home presenter itself, for the places below it (they use its cart).</summary>
        public const string Home = $"{Prefix}-home";
    }

    public HomeKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected HomeKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }

    /// <summary>At least 1; garbage reads as 1. The ceiling is applied when the total is known.</summary>
    public int Page
    {
        get => Intent.GetInt32(P.Page) is > 1 and var page ? page : 1;
        set => Intent.SetParameter(P.Page, value > 1 ? value : null);
    }

    public HomePresenter? Home
    {
        get => Intent.GetAttribute<HomePresenter>(A.Home);
        set => Intent.SetAttribute(A.Home, value);
    }
}
