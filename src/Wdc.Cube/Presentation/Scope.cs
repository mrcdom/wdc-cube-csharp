namespace Wdc.Cube;

/// <summary>
/// The rendering state owned by a presenter (also called ViewState): what the view draws.
/// </summary>
/// <remarks>
/// Minimal for now: observation, the dirty/rendering/clean cycle and the binding to a renderer arrive with the
/// update pipeline (decision D-03).
/// </remarks>
public abstract class Scope
{
    /// <summary>
    /// What makes this scope the same thing on screen across loads: a list reconciles its rows by it. Not observed.
    /// </summary>
    public object? Identity { get; set; }
}
