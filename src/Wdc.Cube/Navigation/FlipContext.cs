namespace Wdc.Cube;

/// <summary>
/// One attempt to change face, from the first <see cref="CubeApplication.FlipToIntent"/> until commit or rollback.
/// </summary>
/// <remarks>
/// <para>
/// A redirect (a navigation started from inside an <see cref="ICubePresenter.ApplyParameters"/>) does not create
/// another context: it retargets this one and raises its <see cref="Level"/>. The walk of the previous level sees
/// the level change and stops. Because there is a single working map for the whole attempt, a presenter created at
/// one level is reused by the next, and whatever is not on the committed path is released exactly once, at commit
/// or at rollback.
/// </para>
/// <para>
/// The application's own map is never touched until commit, so a rollback only has to re-apply the source intent to
/// the presenters that were already there and release the ones created along the way.
/// </para>
/// </remarks>
internal sealed class FlipContext
{
    /// <summary>How many times the same path can be targeted in one attempt before it is a cycle.</summary>
    internal const int MaxVisitsPerPath = 5;

    /// <summary>How many redirects one attempt can take.</summary>
    internal const int MaxLevels = 10;

    private readonly CubeApplication _app;
    private readonly Dictionary<int, ICubePresenter> _presenters;
    private readonly Dictionary<string, int> _visits = new(StringComparer.Ordinal);

    public FlipContext(CubeApplication app, FlipIntent targetIntent, FlipIntent sourceIntent,
        IEnumerable<KeyValuePair<int, ICubePresenter>> livePresenters)
    {
        _app = app;
        SourceIntent = sourceIntent;
        TargetIntent = targetIntent;
        _presenters = new Dictionary<int, ICubePresenter>(livePresenters);
        _visits[targetIntent.Place.PathName] = 1;
    }

    /// <summary>The state before the attempt, used to restore it on rollback.</summary>
    public FlipIntent SourceIntent { get; }

    /// <summary>The intent of the current level.</summary>
    public FlipIntent TargetIntent { get; private set; }

    /// <summary>Raised by every redirect.</summary>
    public int Level { get; private set; }

    /// <summary>The deepest place accepted by the walk of the current level.</summary>
    public Place? AcceptedPlace { get; private set; }

    /// <summary>Whether the attempt is being undone.</summary>
    public bool IsRollingBack { get; private set; }

    /// <summary>Takes a redirect: the new intent becomes the target and the walk restarts one level up.</summary>
    public int Retarget(FlipIntent intent)
    {
        var visits = _visits.GetValueOrDefault(intent.Place.PathName) + 1;
        if (visits > MaxVisitsPerPath || Level + 1 > MaxLevels)
        {
            throw new CubeNavigationException(
                $"Navigation cycle detected: source({SourceIntent}) => target({TargetIntent}); the next redirect was to \"{intent}\".");
        }

        _visits[intent.Place.PathName] = visits;
        TargetIntent = intent;
        AcceptedPlace = null;
        return ++Level;
    }

    /// <summary>
    /// Applies the target intent to the presenter of <paramref name="place"/>, reusing it or creating it.
    /// Returns whether the walk of <paramref name="atLevel"/> may go on to the next place.
    /// </summary>
    public async Task<bool> Step(Place place, int atLevel)
    {
        if (Level != atLevel)
        {
            return false;
        }

        var intent = TargetIntent;
        var last = ReferenceEquals(intent.Place, place);
        bool goAhead;

        if (_presenters.TryGetValue(place.Id, out var presenter))
        {
            goAhead = await presenter.ApplyParameters(intent, initialization: false, last);
        }
        else if (place.Factory is { } factory)
        {
            presenter = factory(_app);
            _presenters[place.Id] = presenter;
            goAhead = await presenter.ApplyParameters(intent, initialization: true, last);
        }
        else
        {
            goAhead = true; // a structural place, with no presenter of its own
        }

        if (Level != atLevel)
        {
            return false; // redirected while applying: the newer level owns the attempt now
        }

        AcceptedPlace = place;
        return goAhead;
    }

    /// <summary>
    /// Keeps the presenters on the path of the committed place and releases every other one, leaves first. When no
    /// place was accepted (the target is the detached root, or the application itself stopped the walk), the face is
    /// the root: no presenter of the tree stays.
    /// </summary>
    public Dictionary<int, ICubePresenter> Commit(out Place committedPlace)
    {
        committedPlace = AcceptedPlace ?? Place.Root;

        var kept = new Dictionary<int, ICubePresenter>();
        foreach (var place in committedPlace.Path)
        {
            if (_presenters.Remove(place.Id, out var presenter))
            {
                kept[place.Id] = presenter;
            }
        }

        ReleaseRemaining();
        return kept;
    }

    /// <summary>
    /// Re-applies the source intent to the presenters that were alive before the attempt (root first) and releases
    /// the ones created by it. Failures while restoring are reported and do not stop the restoration.
    /// </summary>
    public async Task Rollback()
    {
        IsRollingBack = true;

        foreach (var place in SourceIntent.Place.Path)
        {
            if (!_presenters.Remove(place.Id, out var presenter))
            {
                continue;
            }

            try
            {
                await presenter.ApplyParameters(SourceIntent, initialization: false,
                    last: ReferenceEquals(place, SourceIntent.Place));
            }
            catch (Exception caught)
            {
                _app.Unexpected($"Restoring presenter of place '{place}' during rollback", caught);
            }
        }

        ReleaseRemaining();
    }

    private void ReleaseRemaining()
    {
        foreach (var id in _presenters.Keys.OrderDescending().ToList())
        {
            var presenter = _presenters[id];
            _presenters.Remove(id);
            _app.ReleasePresenter(presenter);
        }
    }
}
