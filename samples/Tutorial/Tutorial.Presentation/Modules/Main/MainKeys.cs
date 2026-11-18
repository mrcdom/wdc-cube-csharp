using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Main;

/// <summary>
/// Typed reading of the intent for the application level. The only class that knows these names; every name starts
/// with the file's own prefix so names stay unique across the application.
/// </summary>
public class MainKeys
{
    public const string Prefix = "mn";

    private static class A
    {
        /// <summary>Where the level below publishes its scope (each level republishes it for the next).</summary>
        public const string ParentSlot = $"{Prefix}-parent-slot";

        /// <summary>Where a dialog place publishes its scope.</summary>
        public const string DialogSlot = $"{Prefix}-dialog-slot";
    }

    public MainKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Places.Main)
    {
    }

    protected MainKeys(CubeApplication app, FlipIntent? intent, Place place)
    {
        ArgumentNullException.ThrowIfNull(app);
        App = app;
        Intent = intent ?? app.NewFlipIntent(place);
    }

    protected CubeApplication App { get; }

    protected FlipIntent Intent { get; }

    /// <summary>Navigates to the intent this keys object was built for (from the state).</summary>
    public Task<bool> Flip() => App.FlipToIntent(Intent);

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
