using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Receipt;

public sealed record ReceiptLine(string Description, int Quantity, string ValueLabel);

public sealed class ReceiptScope : Scope
{
    /// <summary>Only right after buying: "Compra realizada com sucesso!".</summary>
    public bool NotifySuccess { get; set => Set(ref field, value); }

    public string DateLabel { get; set => Set(ref field, value); } = string.Empty;

    public IReadOnlyList<ReceiptLine> Lines { get; set => Set(ref field, value); } = [];

    public string TotalLabel { get; set => Set(ref field, value); } = string.Empty;

    public Func<Task> OnContinue { get; set; } = ScopeActions.Unbound();
}
