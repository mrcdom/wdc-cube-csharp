using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Products;

public sealed class ProductsPanelScope : Scope
{
    public IReadOnlyList<ProductCard> Products { get; set => Set(ref field, value); } = [];

    public bool Loading { get; set => Set(ref field, value); }

    /// <summary>"{n} itens".</summary>
    public string CountLabel { get; set => Set(ref field, value); } = string.Empty;

    public Func<long, Task> OnOpenProduct { get; set; } = ScopeActions.Unbound<long>();
}
