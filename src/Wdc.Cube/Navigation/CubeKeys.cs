namespace Wdc.Cube;

/// <summary>
/// The base of an application's keys classes: typed properties over a <see cref="FlipIntent"/>, one class per place.
/// </summary>
/// <remarks>
/// <para>Each keys class declares its place (<c>public static new Place Place { get; }</c>, built from the place of
/// the keys it extends) and the names it owns, all starting with its prefix. Keys of a deeper place extend the keys of
/// the place above, so the class tree is the place tree, and a deeper presenter reads what the levels above hand
/// down.</para>
/// <para>Built without an intent, the keys start from the application's current state
/// (<see cref="CubeApplication.NewFlipIntent"/>) and <see cref="Flip"/> goes to their place:
/// <c>await new ProductKeys(App) { ProductId = id }.Flip();</c>. Built over the intent a presenter receives, they read
/// and write it in place.</para>
/// </remarks>
public abstract class CubeKeys
{
    /// <param name="app">The application.</param>
    /// <param name="intent">The intent to read and write; <see langword="null"/> for a new one, from the current state.</param>
    /// <param name="place">Where a new intent goes: the place of the keys class being built.</param>
    protected CubeKeys(CubeApplication app, FlipIntent? intent, Place place)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(place);
        App = app;
        Intent = intent ?? app.NewFlipIntent(place);
    }

    protected CubeApplication App { get; }

    protected FlipIntent Intent { get; }

    /// <summary>Navigates to the place of this intent, with what was set on it.</summary>
    public Task<bool> Flip() => App.FlipToIntent(Intent);
}
