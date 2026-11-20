using Shopping.Presentation.Modules.Home.Products;
using Shopping.Presentation.Modules.Home.Purchases;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home;

public sealed class HomeScope : Scope
{
    /// <summary>"Olá, {name}".</summary>
    public string? Greeting { get; set => Set(ref field, value); }

    /// <summary>The cart badge: the sum of the quantities.</summary>
    public int CartCount { get; set => Set(ref field, value); }

    /// <summary>What a place below Home shows (product, cart, receipt); null shows the two panels.</summary>
    public Scope? Content { get; set => Set(ref field, value); }

    /// <summary>The products panel (its own presenter).</summary>
    public ProductsPanelScope? Products { get; set => Set(ref field, value); }

    /// <summary>The purchase history panel (its own presenter).</summary>
    public PurchasesPanelScope? Purchases { get; set => Set(ref field, value); }

    /// <summary>Whether the administration is offered (whoever may correct purchases).</summary>
    public bool ShowAdmin { get; set => Set(ref field, value); }

    public Func<Task> OnOpenCart { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnOpenAdmin { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnExit { get; set; } = ScopeActions.Unbound();

    /// <summary>Back to the panels (the logo).</summary>
    public Func<Task> OnHome { get; set; } = ScopeActions.Unbound();
}
