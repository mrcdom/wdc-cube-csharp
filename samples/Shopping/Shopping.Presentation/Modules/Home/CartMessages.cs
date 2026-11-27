using Shopping.I18n;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home;

/// <summary>What the cart says, as messages of the catalog.</summary>
public static class CartMessages
{
    public static Message Empty => Texts.CartEmpty();

    public static Message QuantityBelowOne => Texts.CartQuantityBelowOne();

    public static Message ProductNotInCart => Texts.CartProductNotInCart();

    public static Message Unavailable => Texts.CartUnavailable();
}
