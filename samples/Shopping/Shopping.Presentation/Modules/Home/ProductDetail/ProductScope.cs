using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.ProductDetail;

public sealed class ProductScope : Scope
{
    public long Id { get; set => Set(ref field, value); }

    public string Name { get; set => Set(ref field, value); } = string.Empty;

    /// <summary>The description as a list of items (the view never parses HTML).</summary>
    public IReadOnlyList<string> DescriptionItems { get; set => Set(ref field, value); } = [];

    public string PriceLabel { get; set => Set(ref field, value); } = string.Empty;

    public string ImageUrl { get; set => Set(ref field, value); } = string.Empty;

    public string? ErrorMessage { get; set => Set(ref field, value); }

    /// <summary>The quantity chosen in the view.</summary>
    public Func<int, Task> OnAddToCart { get; set; } = ScopeActions.Unbound<int>();

    public Func<Task> OnBack { get; set; } = ScopeActions.Unbound();
}
