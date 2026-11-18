namespace Wdc.Cube;

/// <summary>
/// The boundary with whatever keeps the address (a browser bar, a deep link, a test). The application writes the
/// identity of its state through <see cref="Update"/> and follows external changes (Back, a pasted link) through
/// <see cref="Changed"/>.
/// </summary>
public abstract class HistoryManager
{
    /// <summary>The current address, always as plain text (<c>path?query</c>).</summary>
    public virtual string Location => string.Empty;

    /// <summary>Raised when the address changes from outside the application.</summary>
    public event Action<HistoryManager>? Changed;

    /// <summary>
    /// Publishes the identity of the application's state. Implementations usually write
    /// <c>app.NewFlipIntent(place).ToString()</c> when it differs from the current address.
    /// </summary>
    public abstract void Update(CubeApplication app, Place place);

    /// <summary>Tells the application that the address changed from outside.</summary>
    protected void NotifyChanges() => Changed?.Invoke(this);
}

/// <summary>A history that keeps nothing: for applications with no address.</summary>
public sealed class NoopHistoryManager : HistoryManager
{
    /// <inheritdoc/>
    public override void Update(CubeApplication app, Place place)
    {
    }
}
