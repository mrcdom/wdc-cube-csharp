namespace Wdc.Cube;

/// <summary>
/// Where a presenter publishes its scope so that the level above shows it. The level above offers the slot through
/// the intent's attributes; the level below calls it with its scope, on every
/// <see cref="ICubePresenter.ApplyParameters"/>. Calling it with <see langword="null"/> clears what is shown.
/// </summary>
public delegate void ScopeSlot(Scope? scope);

/// <summary>Ready-made slots.</summary>
public static class ScopeSlots
{
    /// <summary>A slot that ignores what it receives: what a presenter gets when nobody offered one.</summary>
    public static ScopeSlot Noop { get; } = static _ => { };
}
