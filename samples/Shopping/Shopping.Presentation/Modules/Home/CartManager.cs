using Shopping.Domain;
using Shopping.I18n;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home.Calls;

namespace Shopping.Presentation.Modules.Home;

/// <summary>
/// The cart, in memory while Home lives (logging out or leaving the restricted area empties it). Every change raises
/// <see cref="Changed"/>; a purchase raises <see cref="Committed"/>.
/// </summary>
public sealed class CartManager(ShoppingRepositories repositories, TimeProvider time)
{
    private readonly List<CartLine> _lines = [];

    public event Action? Changed;

    public event Action<long>? Committed;

    public IReadOnlyList<CartLine> Lines => _lines;

    /// <summary>The sum of the quantities (the badge).</summary>
    public int ItemCount => _lines.Sum(l => l.Quantity);

    public decimal Total => _lines.Sum(l => l.Total);

    /// <summary>Adds the product, or adds to its quantity when it is already in the cart.</summary>
    public void Add(long productId, string name, decimal price, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        if (_lines.Find(l => l.ProductId == productId) is { } line)
        {
            line.Quantity += quantity;
        }
        else
        {
            _lines.Add(new CartLine(productId, name, price, quantity));
        }

        Changed?.Invoke(); // in both cases: the badge counts quantities, not lines
    }

    /// <summary>Sets the quantity of a line. Returns false when the product is not in the cart.</summary>
    public bool SetQuantity(long productId, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        if (_lines.Find(l => l.ProductId == productId) is not { } line)
        {
            return false;
        }

        line.Quantity = quantity;
        Changed?.Invoke();
        return true;
    }

    public bool Remove(long productId)
    {
        if (_lines.RemoveAll(l => l.ProductId == productId) == 0)
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_lines.Count > 0)
        {
            _lines.Clear();
            Changed?.Invoke();
        }
    }

    /// <summary>Buys what is in the cart: the purchase and its items in one transaction. The cart is kept on failure.</summary>
    /// <returns>The purchase id.</returns>
    /// <exception cref="InvalidCartItemException">The cart is empty or a line has quantity below one.</exception>
    public async Task<long> Commit(Subject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (_lines.Count == 0)
        {
            throw new InvalidCartItemException(MessageKeys.CartEmpty); // the key of what the cart shows
        }

        if (_lines.Exists(l => l.Quantity < 1))
        {
            throw new InvalidCartItemException(MessageKeys.CartQuantityBelowOne);
        }

        var id = await new PurchaseCreate(repositories, userId: subject.Id, buyDate: time.GetUtcNow(), lines: [.. _lines]).Run();
        _lines.Clear();
        Changed?.Invoke();
        Committed?.Invoke(id);
        return id;
    }
}
