using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Purchases;

public sealed class PurchasesPanelScope : Scope
{
    public IReadOnlyList<PurchaseCard> Purchases { get; set => Set(ref field, value); } = [];

    public bool Loading { get; set => Set(ref field, value); }

    /// <summary>"{n} compras".</summary>
    public string CountLabel { get; set => Set(ref field, value); } = string.Empty;

    /// <summary>"página / total".</summary>
    public string PageLabel { get; set => Set(ref field, value); } = string.Empty;

    public bool HasPrevious { get; set => Set(ref field, value); }

    public bool HasNext { get; set => Set(ref field, value); }

    public Func<Task> OnPrevious { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnNext { get; set; } = ScopeActions.Unbound();

    public Func<long, Task> OnOpenReceipt { get; set; } = ScopeActions.Unbound<long>();
}
