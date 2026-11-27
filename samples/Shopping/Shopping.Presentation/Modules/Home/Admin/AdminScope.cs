using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Admin;

/// <summary>A purchase in the administration's list: whose, when, what, how much, and whether it was corrected.</summary>
public sealed record AdminPurchaseRow(long Id, Message NumberLabel, string CustomerLabel, Message? DateLabel, string ItemsLabel,
    string TotalLabel, bool Corrected);

public sealed class AdminScope : Scope
{
    /// <summary>The purchase being corrected (the place below); null shows the list.</summary>
    public Scope? Content { get; set => Set(ref field, value); }

    /// <summary>Part of the customer's name the list is filtered by.</summary>
    public string CustomerFilter { get; set => Set(ref field, value); } = string.Empty;

    public IReadOnlyList<AdminPurchaseRow> Purchases { get; set => Set(ref field, value); } = [];

    public bool Loading { get; set => Set(ref field, value); }

    /// <summary>"{n} purchases".</summary>
    public Message? CountLabel { get; set => Set(ref field, value); }

    /// <summary>"page / total".</summary>
    public Message? PageLabel { get; set => Set(ref field, value); }

    public bool HasPrevious { get; set => Set(ref field, value); }

    public bool HasNext { get; set => Set(ref field, value); }

    public Func<string, Task> OnFilter { get; set; } = ScopeActions.Unbound<string>();

    public Func<Task> OnPrevious { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnNext { get; set; } = ScopeActions.Unbound();

    public Func<long, Task> OnOpenPurchase { get; set; } = ScopeActions.Unbound<long>();

    public Func<Task> OnBack { get; set; } = ScopeActions.Unbound();
}
