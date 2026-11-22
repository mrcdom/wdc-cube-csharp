using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Admin.Amend;

/// <summary>An errata already made: when, by whom, why, and what changed ("Volleyball: 3 → 1").</summary>
public sealed record AmendmentEntry(string DateLabel, string AuthorLabel, string Reason, IReadOnlyList<string> Changes);

/// <summary>One item of the purchase: what it has, and what it will have after the correction.</summary>
public sealed class AmendLineScope : Scope
{
    public long ItemId { get; set => Set(ref field, value); }

    public string Name { get; set => Set(ref field, value); } = string.Empty;

    public string PriceLabel { get; set => Set(ref field, value); } = string.Empty;

    public int Quantity { get; set => Set(ref field, value); }

    /// <summary>From 0 (removes the item) up to <see cref="Quantity"/> (no change).</summary>
    public int NewQuantity { get; set => Set(ref field, value); }

    public Func<int, Task> OnChangeNewQuantity { get; set; } = ScopeActions.Unbound<int>();
}

public sealed class AmendScope : Scope
{
    public AmendScope() => Lines = new ScopeList<AmendLineScope>(this, nameof(Lines));

    /// <summary>"Purchase #{id}".</summary>
    public string TitleLabel { get; set => Set(ref field, value); } = string.Empty;

    public string CustomerLabel { get; set => Set(ref field, value); } = string.Empty;

    public string DateLabel { get; set => Set(ref field, value); } = string.Empty;

    public ScopeList<AmendLineScope> Lines { get; }

    public string TotalLabel { get; set => Set(ref field, value); } = string.Empty;

    /// <summary>The total with the new quantities.</summary>
    public string NewTotalLabel { get; set => Set(ref field, value); } = string.Empty;

    public string Reason { get; set => Set(ref field, value); } = string.Empty;

    /// <summary>The errata already made, oldest first.</summary>
    public IReadOnlyList<AmendmentEntry> History { get; set => Set(ref field, value); } = [];

    /// <summary>Something to correct and a reason: the confirmation is offered.</summary>
    public bool CanConfirm { get; set => Set(ref field, value); }

    public bool Saving { get; set => Set(ref field, value); }

    public string? ErrorMessage { get; set => Set(ref field, value); }

    /// <summary>"Correction recorded." after confirming.</summary>
    public string? SuccessMessage { get; set => Set(ref field, value); }

    public Func<string, Task> OnChangeReason { get; set; } = ScopeActions.Unbound<string>();

    public Func<Task> OnConfirm { get; set; } = ScopeActions.Unbound();

    /// <summary>Back to the list.</summary>
    public Func<Task> OnBack { get; set; } = ScopeActions.Unbound();
}
