using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Wdc.Cube;

/// <summary>
/// The application presenter: the top of the tree of owners. It owns a scope (the page frame: menu, alerts, the
/// body slot), takes part in every navigation before the first place, publishes its own parameters first, and starts
/// the application from the address (<see cref="KickStart"/>).
/// </summary>
public abstract class ApplicationPresenter<TScope> : CubeApplication, IActionHost
    where TScope : Scope
{
    private bool _initialized;

    /// <summary>Creates the application presenter; its root place is <see cref="Place.Root"/>.</summary>
    protected ApplicationPresenter(TScope scope, HistoryManager history, IEnumerable<Place> places,
        ILogger? logger = null, PresentationDispatcher? dispatcher = null, TimeProvider? timeProvider = null)
        : base(history, places, Place.Root, logger, dispatcher, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Scope = scope;
        UpdateManager = new ScopeUpdateManager(Updates, scope);
        UpdateManager.BeforeScopeUpdate += OnBeforeScopeUpdate;
        UpdateManager.RequestFirstDraw();
    }

    /// <summary>The application's own rendering state.</summary>
    public TScope Scope { get; }

    /// <summary>Collects the application's dirty scopes.</summary>
    public ScopeUpdateManager UpdateManager { get; }

    /// <summary>Whether a guarded action redraws the root scope when nothing was marked. On by default.</summary>
    public bool AutoUpdate { get; set; } = true;

    CubeApplication IActionHost.Application => this;

    /// <summary>
    /// Called before the first place of every navigation, as the root of the path. Returning <see langword="false"/>
    /// stops the walk there (the face becomes the bare root); throwing rolls the navigation back.
    /// </summary>
    /// <param name="intent">The intent in transit.</param>
    /// <param name="initialization"><see langword="true"/> only in <see cref="KickStart"/>.</param>
    /// <param name="last">Whether the intent asks for the root itself.</param>
    public virtual Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last) =>
        Task.FromResult(true);

    /// <summary>Writes the application's own parameters; called before every presenter's.</summary>
    public virtual void PublishParameters(FlipIntent intent)
    {
    }

    /// <summary>
    /// Starts the application from the current address: applies the application's own parameters once
    /// (<c>initialization: true</c>, <c>last: true</c>), then, when the address names a deeper face, flips there.
    /// Any failure sends the user to <paramref name="safePlace"/> instead, without the parameters of the address that
    /// failed.
    /// </summary>
    /// <remarks>
    /// The address is read first: install a history codec before calling this.
    /// </remarks>
    public Task KickStart(Place safePlace)
    {
        ArgumentNullException.ThrowIfNull(safePlace);
        return InvokeAsync(async () =>
        {
            var intent = NewIntentFromString(History.Location);
            try
            {
                await ApplyParameters(intent.Redirect(RootPlace), initialization: true, last: true);
                _initialized = true;
                if (!ReferenceEquals(intent.Place, RootPlace))
                {
                    intent.Attributes.Clear();
                    await FlipCore(intent);
                }
            }
            catch (Exception caught)
            {
                Unexpected($"Starting at '{intent}'; going to '{safePlace}' instead", caught);

                // From the state, not from the address that failed: its parameters may be what made it fail.
                _initialized = true;
                await FlipCore(NewFlipIntent(safePlace));
            }
        });
    }

    /// <summary>Marks <paramref name="scope"/> (the root scope when omitted) dirty by hand.</summary>
    public void Update(Scope? scope = null) => UpdateManager.Update(scope);

    /// <summary>Releases the application's scope and every presenter.</summary>
    public override void Release()
    {
        Dispatcher.VerifyAccess();
        UpdateManager.BeforeScopeUpdate -= OnBeforeScopeUpdate;
        UpdateManager.Release();
        base.Release();
    }

    /// <summary>Runs before each flush that includes the application's scopes: compute derived state here.</summary>
    protected virtual void OnBeforeScopeUpdate()
    {
    }

    /// <inheritdoc/>
    protected override void PublishAllParameters(FlipIntent intent)
    {
        PublishParameters(intent);
        base.PublishAllParameters(intent);
    }

    private protected override async Task<bool> WalkPath(FlipContext context, int level)
    {
        var intent = context.TargetIntent;
        var last = intent.Place.IsDetached;
        var goAhead = await ApplyParameters(intent, initialization: !_initialized, last);
        _initialized = true;
        if (!goAhead || last || context.Level != level)
        {
            return false;
        }

        return await base.WalkPath(context, level);
    }

    // :: Guarded actions (see Presenter.Action).

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

    /// <summary>
    /// Binds a handler that mirrors what the user types: on the dispatcher, but without flush or address update.
    /// </summary>
    /// <remarks>
    /// Write the value back into the property of the scope the box shows (<c>Scope.Email = value</c>): the view draws
    /// the box from it, and a render with an older value would overwrite what the user typed.
    /// </remarks>
    protected Func<T, Task> Mirror<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return arg => Dispatcher.InvokeAsync(() => handler(arg));
    }

    void IActionHost.UpdateIfNotDirty()
    {
        if (AutoUpdate && UpdateManager.UpdateCount == 0)
        {
            UpdateManager.Update();
        }
    }
}
