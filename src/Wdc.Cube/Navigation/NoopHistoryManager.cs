namespace Wdc.Cube;

/// <summary>A history that keeps nothing: for applications with no address.</summary>
public sealed class NoopHistoryManager : HistoryManager
{
    /// <inheritdoc/>
    public override void Update(CubeApplication app, Place place)
    {
    }
}
