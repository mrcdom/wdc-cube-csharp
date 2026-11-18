namespace Wdc.Cube;

/// <summary>
/// The contract of whoever takes part in a face of the cube.
/// </summary>
public interface ICubePresenter
{
    /// <summary>
    /// Accepts the intent and recovers the state from its parameters. Called on each place of the path, from the
    /// root to the leaf, on every navigation that passes through this presenter.
    /// </summary>
    /// <param name="intent">The intent in transit, shared by every level of the path.</param>
    /// <param name="initialization"><see langword="true"/> on the first call after construction; <see langword="false"/> when reused.</param>
    /// <param name="last"><see langword="true"/> only when this place is the leaf the intent asks for.</param>
    /// <returns>
    /// <see langword="true"/> to let the composition continue to the next level. <see langword="false"/> means
    /// <b>accepted, but stop here</b>: the face is committed up to this presenter, even if the intent asked for
    /// something deeper. To refuse the navigation and keep the previous state, throw.
    /// </returns>
    Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last);

    /// <summary>Writes the minimal identification of this presenter's state: what is enough to recreate it.</summary>
    void PublishParameters(FlipIntent intent);

    /// <summary>Releases what needs special handling when the presenter leaves the face. Called exactly once.</summary>
    void Release();
}
