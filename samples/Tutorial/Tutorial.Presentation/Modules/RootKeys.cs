using Wdc.Cube;

namespace Tutorial.Presentation.Modules;

/// <summary>
/// Typed reading of the intent for the application level. The only class that knows these names; every name starts
/// with the file's own prefix so names stay unique across the application.
/// </summary>
public class RootKeys : CubeKeys
{
    public const string Prefix = "rt";

    /// <summary>The application itself (the page frame).</summary>
    public static Place Place { get; } = Place.Root;

    private static class A
    {
        /// <summary>Where the level below publishes its scope (each level republishes it for the next).</summary>
        public const string ParentSlot = $"{Prefix}-parent-slot";

        /// <summary>Where a dialog place publishes its scope.</summary>
        public const string DialogSlot = $"{Prefix}-dialog-slot";
    }

    public RootKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected RootKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }

    public ScopeSlot ParentSlot
    {
        get => Intent.GetScopeSlot(A.ParentSlot);
        set => Intent.SetScopeSlot(A.ParentSlot, value);
    }

    public ScopeSlot DialogSlot
    {
        get => Intent.GetScopeSlot(A.DialogSlot);
        set => Intent.SetScopeSlot(A.DialogSlot, value);
    }
}
