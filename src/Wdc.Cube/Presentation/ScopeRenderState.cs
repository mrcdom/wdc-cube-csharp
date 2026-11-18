namespace Wdc.Cube;

/// <summary>Where a scope is in the render cycle (decision D-03).</summary>
public enum ScopeRenderState
{
    /// <summary>What is on screen matches the scope.</summary>
    Clean,

    /// <summary>Changed; waiting for the next update window.</summary>
    Dirty,

    /// <summary>Its view was told to redraw and has not confirmed yet.</summary>
    Rendering,
}
