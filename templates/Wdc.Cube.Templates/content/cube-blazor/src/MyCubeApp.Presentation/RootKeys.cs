using Wdc.Cube;

namespace MyCubeApp;

/// <summary>
/// The application level: its place (the root) and the names it reads and writes in an intent. Each place has one
/// keys class, which declares the place and is the only one that knows its names: presenters navigate through the
/// keys of the destination (<c>new TaskKeys(App) { TaskId = 3 }.Flip()</c>) and never write a name by hand. The keys
/// of a deeper place extend the keys of the place above, and every name starts with the prefix of its class.
/// </summary>
public class RootKeys : CubeKeys
{
    public const string Prefix = "rt";

    /// <summary>The application itself.</summary>
    public static Place Place { get; } = Place.Root;

    private const string ParentSlotName = $"{Prefix}-parent-slot";

    public RootKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected RootKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }

    /// <summary>
    /// Where this level shows its scope. Each level reads the slot it got, and puts under the same name the slot of
    /// its own content, for the level below.
    /// </summary>
    public ScopeSlot ParentSlot
    {
        get => Intent.GetScopeSlot(ParentSlotName);
        set => Intent.SetScopeSlot(ParentSlotName, value);
    }
}
