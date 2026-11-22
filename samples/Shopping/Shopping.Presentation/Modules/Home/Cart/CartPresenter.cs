using Shopping.Presentation.Modules.Home;
using Shopping.Domain;
using Shopping.Presentation.Infra;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Cart;

/// <summary>The cart, under Home (whose cart it shows). Messages go away after a few seconds.</summary>
public sealed class CartPresenter(RootPresenter app) : CubePresenter<RootPresenter, CartScope>(app, new CartScope())
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
            Scope.OnContinue = Action(() => new HomeKeys(App).Flip());
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
            await new HomeKeys(App).Flip(); // nothing left to buy
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
        catch (Exception caught) when (caught is System.Data.Common.DbException or HttpRequestException)
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
