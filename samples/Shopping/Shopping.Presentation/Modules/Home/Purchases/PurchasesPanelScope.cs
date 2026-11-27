using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Purchases;

/// <summary>A purchase card: date, what was bought, total.</summary>
public sealed record PurchaseCard(long Id, Message? DateLabel, string ItemsLabel, string TotalLabel);

public sealed class PurchasesPanelScope : Scope
{
    public IReadOnlyList<PurchaseCard> Purchases { get; set => Set(ref field, value); } = [];

    public bool Loading { get; set => Set(ref field, value); }

    /// <summary>"{n} purchases".</summary>
    public Message? CountLabel { get; set => Set(ref field, value); }

    /// <summary>"page / total".</summary>
    public Message? PageLabel { get; set => Set(ref field, value); }

    public bool HasPrevious { get; set => Set(ref field, value); }

    public bool HasNext { get; set => Set(ref field, value); }

    public Func<Task> OnPrevious { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnNext { get; set; } = ScopeActions.Unbound();

    public Func<long, Task> OnOpenReceipt { get; set; } = ScopeActions.Unbound<long>();
}
