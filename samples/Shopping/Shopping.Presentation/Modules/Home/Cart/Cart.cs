using Shopping.Domain;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Main;
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

public sealed class CartScope : Scope
{
    public CartScope() => Lines = new ScopeList<CartLineScope>(this, nameof(Lines));

    public ScopeList<CartLineScope> Lines { get; }

    public string TotalLabel { get; set => Set(ref field, value); } = string.Empty;

    public string? ErrorMessage { get; set => Set(ref field, value); }

    public bool Buying { get; set => Set(ref field, value); }

    public Func<Task> OnBuy { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnContinue { get; set; } = ScopeActions.Unbound();
}

/// <summary>The cart, under Home (whose cart it shows). Messages go away after a few seconds.</summary>
public sealed class CartPresenter(MainPresenter app) : CubePresenter<MainPresenter, CartScope>(app, new CartScope())
{
    public static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(3);

    private HomePresenter? _home;
    private ITimer? _messageTimer;

    private CartManager Cart => _home?.Cart ?? throw new InvalidOperationException("The cart is shown outside Home.");

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new HomeKeys(App, intent);
        if (!ReferenceEquals(_home, keys.Home))
        {
            if (_home is not null)
            {
                _home.Cart.Changed -= Refresh;
            }

            _home = keys.Home;
            _home!.Cart.Changed += Refresh;
        }

        if (initialization)
        {
            Scope.OnBuy = Action(OnBuy);
            Scope.OnContinue = Action(() => Flip(Places.Home));
        }

        Refresh();
        keys.ParentSlot(Scope);
        return Task.FromResult(true);
    }

    public override void Release()
    {
        if (_home is not null)
        {
            _home.Cart.Changed -= Refresh;
        }

        _messageTimer?.Dispose();
        base.Release();
    }

    /// <summary>Reconciles the lines by product: a line that stays keeps its scope.</summary>
    private void Refresh()
    {
        var current = Scope.Lines.ToDictionary(l => l.ProductId);
        Scope.Lines.Assign([.. Cart.Lines.Select(line =>
        {
            var row = current.GetValueOrDefault(line.ProductId) ?? NewRow(line.ProductId);
            row.Identity = line.ProductId;
            row.Name = line.Name;
            row.PriceLabel = Formats.Money(line.Price);
            row.Quantity = line.Quantity;
            row.ImageUrl = Formats.ProductImageUrl(line.ProductId);
            return row;
        })]);
        Scope.TotalLabel = Formats.Money(Cart.Total);
    }

    private CartLineScope NewRow(long productId) => new()
    {
        ProductId = productId,
        OnChangeQuantity = Action<int>(quantity => ChangeQuantity(productId, quantity)),
        OnRemove = Action(() => Remove(productId)),
    };

    private void ChangeQuantity(long productId, int quantity)
    {
        if (quantity < 1)
        {
            Show(CartMessages.QuantityBelowOne);
            return;
        }

        if (!Cart.SetQuantity(productId, quantity))
        {
            Show(CartMessages.ProductNotInCart);
        }
    }

    private async Task Remove(long productId)
    {
        if (Cart.Remove(productId) && Cart.Lines.Count == 0)
        {
            await Flip(Places.Home); // nothing left to buy
        }
    }

    private async Task OnBuy()
    {
        var subject = App.Subject ?? throw new InvalidOperationException("No session.");
        Scope.Buying = true;
        try
        {
            var purchaseId = await Cart.Commit(subject);
            await new Receipt.ReceiptKeys(App) { PurchaseId = purchaseId, PurchaseMade = true }.Flip();
        }
        catch (InvalidCartItemException caught)
        {
            Show(caught.Message);
        }
        catch (System.Data.Common.DbException caught)
        {
            App.Unexpected("Registrando a compra", caught);
            Show(CartMessages.Unavailable);
        }
        finally
        {
            if (!IsReleased)
            {
                Scope.Buying = false;
            }
        }
    }

    private void Show(string message)
    {
        Scope.ErrorMessage = message;
        _messageTimer?.Dispose(); // a new message restarts the countdown
        _messageTimer = App.Time.CreateTimer(_ => App.Dispatcher.Post(() =>
        {
            if (!IsReleased && Scope.ErrorMessage == message)
            {
                Scope.ErrorMessage = null;
            }
        }), null, MessageDuration, Timeout.InfiniteTimeSpan);
    }
}
