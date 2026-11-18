namespace Wdc.Cube;

/// <summary>
/// A presenter that takes part in a face of the cube: it has a place, accepts or refuses intents and publishes the
/// identity of its state.
/// </summary>
/// <remarks>
/// Template:
/// <code>
/// public override async Task&lt;bool&gt; ApplyParameters(FlipIntent intent, bool initialization, bool last)
/// {
///     var keys = new OrdersKeys(intent);
///     if (initialization)
///     {
///         Scope.OnSave = Action(OnSave);       // bind every action, once
///     }
///     _parentSlot = keys.ParentSlot;           // EVERY call: decided by the level above
///     _parentSlot(Scope);                      // the screen appears before any wait
///     _filter = keys.Filter;                   // the state that identifies the face
///     _ = Load();                              // the screen's own data loads in parallel
///     return true;
/// }
/// </code>
/// </remarks>
public abstract class CubePresenter<TApp, TScope> : Presenter<TApp, TScope>, ICubePresenter
    where TApp : CubeApplication
    where TScope : Scope
{
    /// <summary>Creates the presenter.</summary>
    protected CubePresenter(TApp app, TScope scope)
        : base(app, scope)
    {
    }

    /// <inheritdoc/>
    public virtual Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last) =>
        Task.FromResult(true);

    /// <inheritdoc/>
    public virtual void PublishParameters(FlipIntent intent)
    {
    }
}
