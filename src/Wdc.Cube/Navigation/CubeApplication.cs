using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Wdc.Cube;

/// <summary>
/// Holds the presenters alive on the current face and changes face on request. The state of the application at a
/// given moment is the sum of these presenters.
/// </summary>
/// <remarks>
/// <para>Navigation guarantees (decision D-02):</para>
/// <list type="bullet">
/// <item>every presenter created gets <see cref="ICubePresenter.Release"/> exactly once, leaves first;</item>
/// <item>a presenter that returns <see langword="false"/> is accepted and the face stops there;</item>
/// <item>an exception rolls the attempt back: the previous presenters are restored and the new ones released;</item>
/// <item>redirects reuse what the attempt already created, and a redirect cycle is stopped with
/// <see cref="CubeNavigationException"/>;</item>
/// <item>the address is republished at the end, on success or failure.</item>
/// </list>
/// <para>
/// All presentation code runs on <see cref="Dispatcher"/> (decision D-01). Members returning a <see cref="Task"/>
/// marshal themselves; synchronous members that read or change the navigation state require the caller to be on it.
/// </para>
/// </remarks>
public class CubeApplication
{
    private readonly Dictionary<string, Place> _placesByPath = new(StringComparer.Ordinal);
    private Dictionary<int, ICubePresenter> _presenters = [];
    private FlipContext? _flip;
    private Place? _lastPlace;
    private volatile int _releasePhase;
    private readonly bool _ownsDispatcher;

    /// <summary>Creates the application.</summary>
    /// <param name="history">Where the identity of the state is published.</param>
    /// <param name="places">Every place an address can name.</param>
    /// <param name="rootPlace">The fallback place; <see cref="Place.Root"/> when omitted.</param>
    /// <param name="logger">Receives unexpected errors; nothing is logged when omitted.</param>
    /// <param name="dispatcher">
    /// Where the presentation code runs. When omitted, the application creates a <see cref="SerialDispatcher"/> and
    /// disposes it on <see cref="Release"/>.
    /// </param>
    public CubeApplication(HistoryManager history, IEnumerable<Place> places, Place? rootPlace = null,
        ILogger? logger = null, PresentationDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(places);

        History = history;
        RootPlace = rootPlace ?? Place.Root;
        Logger = logger ?? NullLogger.Instance;
        _ownsDispatcher = dispatcher is null;
        Dispatcher = dispatcher ?? new SerialDispatcher();
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;

        foreach (var place in places)
        {
            if (!_placesByPath.TryAdd(place.PathName, place) && !ReferenceEquals(_placesByPath[place.PathName], place))
            {
                throw new ArgumentException($"Two places share the path '{place.PathName}'.", nameof(places));
            }
        }

        History.Changed += OnHistoryChanged;
    }

    /// <summary>Where the identity of the state is published.</summary>
    public HistoryManager History { get; }

    /// <summary>Where every piece of presentation code of this application runs, one at a time.</summary>
    public PresentationDispatcher Dispatcher { get; }

    /// <summary>The fallback place: where an unknown address and a fresh application resolve to.</summary>
    public Place RootPlace { get; }

    /// <summary>
    /// The place last committed, or <see cref="RootPlace"/> before any navigation. Use <see cref="HasNavigated"/> to
    /// tell "at the root" from "nowhere yet".
    /// </summary>
    public Place LastPlace => _lastPlace ?? RootPlace;

    /// <summary>
    /// Whether a navigation has been committed. False during the first navigation too, including for every
    /// presenter a deep link builds on the way in.
    /// </summary>
    public bool HasNavigated => _lastPlace is not null;

    /// <summary>Whether a navigation attempt is in progress.</summary>
    public bool IsFlipping => _flip is not null;

    /// <summary>Whether <see cref="Release"/> has been called.</summary>
    public bool IsReleased => _releasePhase > 0;

    /// <summary>How many presenters are alive.</summary>
    public int PresenterCount => _presenters.Count;

    /// <summary>Receives unexpected errors.</summary>
    protected ILogger Logger { get; }

    /// <summary>Every place an address can name, by path.</summary>
    public IReadOnlyDictionary<string, Place> Places => _placesByPath;

    // :: Presenters

    /// <summary>The presenter alive at <paramref name="place"/>, or <see langword="null"/>.</summary>
    public ICubePresenter? GetPresenter(Place place)
    {
        ArgumentNullException.ThrowIfNull(place);
        return _presenters.GetValueOrDefault(place.Id);
    }

    /// <summary>The presenter alive at <paramref name="place"/> as <typeparamref name="T"/>, or <see langword="null"/>.</summary>
    public T? GetPresenter<T>(Place place)
        where T : class, ICubePresenter => GetPresenter(place) as T;

    // :: Intents

    /// <summary>
    /// A new intent for <paramref name="place"/>, already filled in by every live presenter: a trip starts from the
    /// state, not from a copy of the address. During a navigation it also carries the attributes in transit.
    /// </summary>
    public FlipIntent NewFlipIntent(Place place)
    {
        Dispatcher.VerifyAccess();
        var intent = new FlipIntent(place);
        intent.CopyAttributesFrom(_flip?.TargetIntent.Attributes);
        PublishAllParameters(intent);
        return intent;
    }

    /// <summary>
    /// An intent read from an address. An unknown path resolves to the last place (or the root); an empty address
    /// to the last place with no parameters.
    /// </summary>
    public FlipIntent NewIntentFromString(string? address)
    {
        Dispatcher.VerifyAccess();
        var fallback = LastPlace;
        var intent = string.IsNullOrEmpty(address)
            ? new FlipIntent(fallback)
            : FlipIntent.Parse(address, path => _placesByPath.GetValueOrDefault(path) ?? fallback);
        intent.CopyAttributesFrom(_flip?.TargetIntent.Attributes);
        return intent;
    }

    /// <summary>Asks every live presenter, root first, to write the identity of its state.</summary>
    protected virtual void PublishAllParameters(FlipIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        foreach (var id in _presenters.Keys.Order())
        {
            _presenters[id].PublishParameters(intent);
        }
    }

    // :: Navigation

    /// <summary>Flips to <paramref name="place"/>, starting from the current state.</summary>
    /// <param name="place">The face to reach.</param>
    /// <param name="configure">Sets what the destination needs (parameters, attributes).</param>
    public Task<bool> Flip(Place place, Action<FlipIntent>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(place);
        return Enter(() =>
        {
            var intent = NewFlipIntent(place);
            configure?.Invoke(intent);
            return FlipCore(intent);
        });
    }

    /// <summary>Flips to the face named by an address.</summary>
    public Task<bool> FlipToIntentString(string? address) => Enter(() => FlipCore(NewIntentFromString(address)));

    /// <summary>
    /// Brings the application to the face the intent asks for.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the committed face is the one the intent asked for; <see langword="false"/> when a
    /// presenter stopped the composition earlier or redirected elsewhere. Inside another navigation (a redirect), it
    /// retargets that navigation and returns whether its walk reached the leaf; the outer navigation commits.
    /// </returns>
    /// <exception cref="CubeNavigationException">Presenters keep redirecting to one another.</exception>
    public Task<bool> FlipToIntent(FlipIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return Enter(() => FlipCore(intent));
    }

    /// <summary>Runs <paramref name="work"/> on <see cref="Dispatcher"/> (inline when already there).</summary>
    public Task InvokeAsync(Func<Task> work) => Dispatcher.InvokeAsync(work);

    /// <summary>Runs <paramref name="action"/> on <see cref="Dispatcher"/> (inline when already there).</summary>
    public Task InvokeAsync(Action action) => Dispatcher.InvokeAsync(action);

    /// <summary>Runs <paramref name="work"/> on <see cref="Dispatcher"/> and returns its result.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work) => Dispatcher.InvokeAsync(work);

    private Task<T> Enter<T>(Func<Task<T>> work) =>
        IsReleased
            ? Task.FromException<T>(new ObjectDisposedException(GetType().Name))
            : Dispatcher.InvokeAsync(work);

    private async Task<bool> FlipCore(FlipIntent intent)
    {
        ObjectDisposedException.ThrowIf(IsReleased, this);

        if (_flip is { } running)
        {
            if (running.IsRollingBack)
            {
                Logger.LogWarning("Ignoring a navigation to '{Intent}' requested while rolling back.", intent);
                return false;
            }

            var level = running.Retarget(intent);
            return await WalkPath(running, level);
        }

        var context = new FlipContext(this, intent, NewFlipIntent(LastPlace), _presenters);
        _flip = context;
        try
        {
            await WalkPath(context, 0);

            _presenters = context.Commit(out var committedPlace);
            _lastPlace = committedPlace;
            return ReferenceEquals(committedPlace, intent.Place);
        }
        catch
        {
            await context.Rollback();
            throw;
        }
        finally
        {
            _flip = null;
            SafeUpdateHistory();
        }
    }

    /// <summary>Walks the path of the context's target, at <paramref name="level"/>, from the root down.</summary>
    private protected virtual async Task<bool> WalkPath(FlipContext context, int level)
    {
        foreach (var place in context.TargetIntent.Place.Path)
        {
            if (!await context.Step(place, level))
            {
                return false;
            }
        }

        return true;
    }

    // :: History

    /// <summary>Republishes the identity of the state, without navigating.</summary>
    public virtual void UpdateHistory()
    {
        Dispatcher.VerifyAccess();
        History.Update(this, LastPlace);
    }

    private void SafeUpdateHistory()
    {
        try
        {
            UpdateHistory();
        }
        catch (Exception caught)
        {
            Unexpected("Updating history", caught);
        }
    }

    // The history may change on any thread (a browser callback, a test): look at it on the dispatcher.
    private void OnHistoryChanged(HistoryManager sender)
    {
        if (!IsReleased)
        {
            Dispatcher.Post(() => FollowHistoryChange(sender));
        }
    }

    private void FollowHistoryChange(HistoryManager sender)
    {
        if (_flip is not null || IsReleased)
        {
            return;
        }

        var previous = NewFlipIntent(LastPlace);
        var location = sender.Location;
        if (string.Equals(location, previous.ToString(), StringComparison.Ordinal))
        {
            return;
        }

        _ = FollowHistory(location, previous);
    }

    private async Task FollowHistory(string location, FlipIntent previous)
    {
        try
        {
            await FlipToIntentString(location);
        }
        catch (Exception caught)
        {
            Unexpected($"Invalid history location '{location}'", caught);
            try
            {
                await FlipToIntent(previous);
            }
            catch (Exception caughtAgain)
            {
                Unexpected("Returning to the previous location", caughtAgain);
            }
        }
    }

    // :: Errors and lifecycle

    /// <summary>Reports an error nobody handled. Override to show it to the user.</summary>
    public virtual void Unexpected(string message, Exception error) =>
        Logger.LogError(error, "{Message}", message);

    /// <summary>
    /// Releases every presenter, leaves first, and stops following the history. A dispatcher created by the
    /// application is disposed; queued items still run.
    /// </summary>
    public virtual void Release()
    {
        Dispatcher.VerifyAccess();
        if (_releasePhase > 0)
        {
            return;
        }

        _releasePhase = 1;
        try
        {
            History.Changed -= OnHistoryChanged;
            foreach (var id in _presenters.Keys.OrderDescending().ToList())
            {
                ReleasePresenter(_presenters[id]);
            }

            _presenters.Clear();
        }
        finally
        {
            _releasePhase = 2;
            Dispatcher.UnhandledException -= OnDispatcherUnhandledException;
            if (_ownsDispatcher && Dispatcher is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private void OnDispatcherUnhandledException(Exception error) =>
        Unexpected("Unhandled error on the presentation dispatcher", error);

    internal void ReleasePresenter(ICubePresenter presenter)
    {
        try
        {
            presenter.Release();
        }
        catch (Exception caught)
        {
            Unexpected($"Releasing presenter {presenter.GetType().Name}", caught);
        }
    }
}
