namespace Shopping.Presentation.Modules.Home;

/// <summary>One line of the cart: the product and the unit price when it was added.</summary>
public sealed class CartLine(long productId, string name, decimal price, int quantity)
{
    public long ProductId { get; } = productId;

    public string Name { get; } = name;

    public decimal Price { get; } = price;

    public int Quantity { get; internal set; } = quantity;

    public decimal Total => Price * Quantity;
}
