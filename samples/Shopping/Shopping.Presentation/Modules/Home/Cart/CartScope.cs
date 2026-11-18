using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Cart;

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
