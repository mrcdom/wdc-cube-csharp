namespace Wdc.Persistence;

/// <summary>A statement ready to run: text with <c>@pN</c> placeholders and their values, in order.</summary>
public sealed record SqlStatement(string Text, IReadOnlyList<object?> Parameters)
{
    public override string ToString() => Text;
}
