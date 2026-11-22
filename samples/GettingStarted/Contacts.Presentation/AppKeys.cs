using Wdc.Cube;

namespace Contacts;

// <snippet:app-keys>
/// <summary>
/// The application level: its place (the root) and the names it reads and writes in an intent. The keys of each
/// deeper place extend the keys of the place above, and own a prefix, so names never clash.
/// </summary>
public class AppKeys : CubeKeys
{
    public const string Prefix = "ap";

    /// <summary>The application itself.</summary>
    public static Place Place { get; } = Place.Root;

    private const string ParentSlotName = $"{Prefix}-parent-slot";

    public AppKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected AppKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }

    /// <summary>Where the level below publishes its scope.</summary>
    public ScopeSlot ParentSlot
    {
        get => Intent.GetScopeSlot(ParentSlotName);
        set => Intent.SetScopeSlot(ParentSlotName, value);
    }
}
// </snippet:app-keys>
