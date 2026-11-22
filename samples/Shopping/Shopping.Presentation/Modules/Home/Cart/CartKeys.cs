using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Cart;

/// <summary>The cart has no names of its own: what it shows lives in Home's cart.</summary>
public sealed class CartKeys : HomeKeys
{
    public new const string Prefix = "ct";

    public static new Place Place { get; } = HomeKeys.Place.Child<RootPresenter>("cart", app => new CartPresenter(app));

    public CartKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }
}
