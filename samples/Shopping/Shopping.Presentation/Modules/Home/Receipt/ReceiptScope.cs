using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Receipt;

public sealed record ReceiptLine(string Description, int Quantity, string ValueLabel);

public sealed class ReceiptScope : Scope
{
    /// <summary>Only right after buying: "Purchase completed successfully!".</summary>
    public bool NotifySuccess { get; set => Set(ref field, value); }

    public Message? DateLabel { get; set => Set(ref field, value); }

    public IReadOnlyList<ReceiptLine> Lines { get; set => Set(ref field, value); } = [];

    public string TotalLabel { get; set => Set(ref field, value); } = string.Empty;

    /// <summary>One per errata, oldest first: "Purchase corrected on {date}: {reason}".</summary>
    public IReadOnlyList<Message> Corrections { get; set => Set(ref field, value); } = [];

    public Func<Task> OnContinue { get; set; } = ScopeActions.Unbound();
}
