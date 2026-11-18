using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Cart;

/// <summary>One line on screen. The same product keeps the same scope while it stays in the cart.</summary>
public sealed class CartLineScope : Scope
{
    public long ProductId { get; set => Set(ref field, value); }

    public string Name { get; set => Set(ref field, value); } = string.Empty;

    public string PriceLabel { get; set => Set(ref field, value); } = string.Empty;

    public int Quantity { get; set => Set(ref field, value); }

    public string ImageUrl { get; set => Set(ref field, value); } = string.Empty;

    public Func<int, Task> OnChangeQuantity { get; set; } = ScopeActions.Unbound<int>();

    public Func<Task> OnRemove { get; set; } = ScopeActions.Unbound();
}
