using Wdc.Cube;

namespace Contacts;

// <snippet:app-keys>
/// <summary>
/// The names the application reads and writes in an intent. Deeper places extend it; each keys class owns a prefix,
/// so names never clash.
/// </summary>
public class AppKeys
{
    public const string Prefix = "ap";

    private const string ParentSlotName = $"{Prefix}-parent-slot";

    public AppKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place.Root)
    {
    }

    protected AppKeys(CubeApplication app, FlipIntent? intent, Place place)
    {
        App = app;
        Intent = intent ?? app.NewFlipIntent(place);
    }

    protected CubeApplication App { get; }

    protected FlipIntent Intent { get; }

    /// <summary>Navigates to the place this object was built for, with what was set on it.</summary>
    public Task<bool> Flip() => App.FlipToIntent(Intent);

    /// <summary>Where the level below publishes its scope.</summary>
    public ScopeSlot ParentSlot
    {
        get => Intent.GetScopeSlot(ParentSlotName);
        set => Intent.SetScopeSlot(ParentSlotName, value);
    }
}
// </snippet:app-keys>
