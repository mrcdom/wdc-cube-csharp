namespace Wdc.Cube;

/// <summary>Ready-made slots.</summary>
public static class ScopeSlots
{
    /// <summary>A slot that ignores what it receives: what a presenter gets when nobody offered one.</summary>
    public static ScopeSlot Noop { get; } = static _ => { };
}
