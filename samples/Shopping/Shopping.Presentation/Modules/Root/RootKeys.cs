using Wdc.Cube;

namespace Shopping.Presentation.Modules.Root;

/// <summary>Names the application level reads and writes. Every keys file owns a two-letter prefix.</summary>
public class RootKeys : CubeKeys
{
    public const string Prefix = "rt";

    /// <summary>The application itself; going there means going home.</summary>
    public static Place Place { get; } = Place.Root;

    private static class A
    {
        /// <summary>Where the level below publishes its scope; each level republishes it for the next.</summary>
        public const string ParentSlot = $"{Prefix}-parent-slot";
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
}
