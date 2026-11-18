using System.Runtime.CompilerServices;

namespace Wdc.Cube;

/// <summary>
/// Owns a part of the application state: its scope (what the view draws) and its fields (the control state).
/// The base of every presenter, including the children that take care of a segment of a screen (a tab, a panel)
/// without a place of their own; those are created and released by their owner.
/// </summary>
public abstract class Presenter<TApp, TScope> : IActionHost
    where TApp : CubeApplication
    where TScope : Scope
{
    private int _releasePhase;

    /// <summary>Creates the presenter and takes <paramref name="scope"/> (and its child scopes).</summary>
    protected Presenter(TApp app, TScope scope)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(scope);
        App = app;
        Scope = scope;
        UpdateManager = new ScopeUpdateManager(app.Updates, scope);
        UpdateManager.BeforeScopeUpdate += OnBeforeScopeUpdate;
        if (app.Dispatcher.CheckAccess())
        {
            UpdateManager.Update(); // the first draw
        }
    }

    /// <summary>The application.</summary>
    public TApp App { get; }

    /// <summary>The rendering state this presenter owns.</summary>
    public TScope Scope { get; }

    /// <summary>Collects this presenter's dirty scopes.</summary>
    public ScopeUpdateManager UpdateManager { get; }

    /// <summary>
    /// Whether a guarded action redraws the root scope at its end when nothing was marked meanwhile: a safety net for
    /// changes <see cref="Scope"/> setters cannot see. On by default.
    /// </summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Whether <see cref="Release"/> has been called.</summary>
    public bool IsReleased => _releasePhase > 0;

    CubeApplication IActionHost.Application => App;

    /// <summary>Marks <paramref name="scope"/> (the root scope when omitted) dirty by hand.</summary>
    public void Update(Scope? scope = null) => UpdateManager.Update(scope);

    /// <summary>Republishes the identity of the state (after changing what <c>PublishParameters</c> writes).</summary>
    public void UpdateHistory() => App.UpdateHistory();

    /// <summary>Reports an error nobody handled.</summary>
    public void Unexpected(string message, Exception error) => App.Unexpected(message, error);

    /// <summary>Flips to <paramref name="place"/>, starting from the current state.</summary>
    public Task<bool> Flip(Place place, Action<FlipIntent>? configure = null) => App.Flip(place, configure);

    /// <summary>
    /// Releases what needs special handling. Called exactly once: by the navigation for a presenter with a place, by
    /// the owner for a child presenter. Overrides must call the base.
    /// </summary>
    public virtual void Release()
    {
        if (_releasePhase > 0)
        {
            return;
        }

        _releasePhase = 1;
        UpdateManager.BeforeScopeUpdate -= OnBeforeScopeUpdate;
        UpdateManager.Release();
        _releasePhase = 2;
    }

    /// <summary>
    /// Runs at the start of each flush that includes this presenter's scopes, before any view is told: compute
    /// derived state here, once, seeing every change.
    /// </summary>
    protected virtual void OnBeforeScopeUpdate()
    {
    }

    // :: Guarded actions. Bind them once, inside `if (initialization)`: Scope.OnSave = Action(OnSave);
    // Leave unguarded only what mirrors typing (it must not flush nor touch the address on every key).

    /// <summary>Guards an asynchronous action.</summary>
    protected Func<Task> Action(Func<Task> action, [CallerArgumentExpression(nameof(action))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ActionGuard.Wrap(this, action, name);
    }

    /// <summary>Guards a synchronous action.</summary>
    protected Func<Task> Action(System.Action action, [CallerArgumentExpression(nameof(action))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ActionGuard.Wrap(this, () =>
        {
            action();
            return Task.CompletedTask;
        }, name);
    }

    /// <summary>Guards an asynchronous action with one argument.</summary>
    protected Func<T, Task> Action<T>(Func<T, Task> action, [CallerArgumentExpression(nameof(action))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return arg => ActionGuard.Wrap(this, () => action(arg), name)();
    }

    /// <summary>Guards a synchronous action with one argument.</summary>
    protected Func<T, Task> Action<T>(Action<T> action, [CallerArgumentExpression(nameof(action))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return arg => ActionGuard.Wrap(this, () =>
        {
            action(arg);
            return Task.CompletedTask;
        }, name)();
    }

    /// <summary>Guards an asynchronous action with two arguments.</summary>
    protected Func<T1, T2, Task> Action<T1, T2>(Func<T1, T2, Task> action,
        [CallerArgumentExpression(nameof(action))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (a, b) => ActionGuard.Wrap(this, () => action(a, b), name)();
    }

    /// <summary>Guards an asynchronous action with three arguments.</summary>
    protected Func<T1, T2, T3, Task> Action<T1, T2, T3>(Func<T1, T2, T3, Task> action,
        [CallerArgumentExpression(nameof(action))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (a, b, c) => ActionGuard.Wrap(this, () => action(a, b, c), name)();
    }

    /// <summary>
    /// Binds a handler that mirrors what the user types: it runs on the dispatcher but is not guarded — no immediate
    /// flush, no address update, no history entry per keystroke. Debounce and search stay in the presenter.
    /// </summary>
    /// <remarks>
    /// Write the value back into the property of the scope the box shows (<c>Scope.Email = value</c>): the view draws
    /// the box from it, and a render with an older value would overwrite what the user typed.
    /// </remarks>
    protected Func<T, Task> Mirror<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return arg => App.Dispatcher.InvokeAsync(() => handler(arg));
    }

    void IActionHost.UpdateIfNotDirty()
    {
        if (AutoUpdate && UpdateManager.UpdateCount == 0)
        {
            UpdateManager.Update();
        }
    }
}
