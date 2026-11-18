using Wdc.Cube;

namespace Shopping.Presentation.Modules.Main;

/// <summary>Names the application level reads and writes. Every keys file owns a two-letter prefix.</summary>
public class MainKeys
{
    public const string Prefix = "mn";

    private static class A
    {
        /// <summary>Where the level below publishes its scope; each level republishes it for the next.</summary>
        public const string ParentSlot = $"{Prefix}-parent-slot";
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

    public Task<bool> Flip() => App.FlipToIntent(Intent);

    public ScopeSlot ParentSlot
    {
        get => Intent.GetScopeSlot(A.ParentSlot);
        set => Intent.SetScopeSlot(A.ParentSlot, value);
    }
}
