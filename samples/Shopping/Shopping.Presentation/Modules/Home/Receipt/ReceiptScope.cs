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

    /// <summary>One per errata (decision D-25), oldest first: "Compra corrigida em {data}: {motivo}".</summary>
    public IReadOnlyList<string> Corrections { get; set => Set(ref field, value); } = [];

    public Func<Task> OnContinue { get; set; } = ScopeActions.Unbound();
}
