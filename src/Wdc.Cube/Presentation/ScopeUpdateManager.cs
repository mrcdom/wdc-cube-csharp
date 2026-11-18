namespace Wdc.Cube;

/// <summary>
/// Collects the dirty scopes of one presenter (its root scope and the ones it adopted) and hands them to their views
/// when the application's <see cref="UpdateScheduler"/> flushes.
/// </summary>
public sealed class ScopeUpdateManager
{
    private readonly UpdateScheduler _scheduler;
    private readonly List<Scope> _dirty = [];
    private readonly Dictionary<Type, (Scope Fallback, int Max)> _hints = [];
    private bool _released;

    /// <summary>Creates the manager and takes <paramref name="root"/> (and the children it already holds).</summary>
    public ScopeUpdateManager(UpdateScheduler scheduler, Scope root)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(root);
        if (root.Manager is not null)
        {
            throw new InvalidOperationException($"{root.GetType().Name} already belongs to another presenter.");
        }

        _scheduler = scheduler;
        Root = root;
        root.AdoptInto(this);
    }

    /// <summary>The presenter's own scope.</summary>
    public Scope Root { get; }

    /// <summary>Where the presentation code runs.</summary>
    public PresentationDispatcher Dispatcher => _scheduler.Dispatcher;

    /// <summary>How many times a scope of this manager was marked dirty since the last flush.</summary>
    public int UpdateCount { get; private set; }

    /// <summary>
    /// Raised at the start of each flush that includes this manager, before any view is told: the place to compute
    /// derived state once, seeing every change. Changes made here go out in the same flush.
    /// </summary>
    public event Action? BeforeScopeUpdate;

    /// <summary>Marks <paramref name="scope"/> (the root when omitted) dirty, as if a property had changed.</summary>
    public void Update(Scope? scope = null) => (scope ?? Root).MarkDirty(null);

    /// <summary>
    /// When more than <paramref name="maxUpdates"/> scopes of type <typeparamref name="TScope"/> are dirty in one
    /// flush, tell <paramref name="fallback"/> to redraw instead of each of them. Pays off only after the list reuses
    /// its row scopes; on its own it saves nothing.
    /// </summary>
    public void Hint<TScope>(Scope fallback, int maxUpdates = 10)
        where TScope : Scope
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentOutOfRangeException.ThrowIfNegative(maxUpdates);
        _hints[typeof(TScope)] = (fallback, maxUpdates);
    }

    /// <summary>Removes the hint for <typeparamref name="TScope"/>.</summary>
    public void RemoveHint<TScope>()
        where TScope : Scope => _hints.Remove(typeof(TScope));

    /// <summary>Stops collecting: pending updates are dropped and later changes are ignored.</summary>
    public void Release()
    {
        _released = true;
        foreach (var scope in _dirty)
        {
            scope.ResetRenderState();
        }

        _dirty.Clear();
        Root.ResetRenderState();
        _scheduler.Forget(this);
    }

    internal void VerifyAccess() => _scheduler.Dispatcher.VerifyAccess();

    internal void Enqueue(Scope scope)
    {
        if (_released)
        {
            scope.ResetRenderState();
            return;
        }

        UpdateCount++;
        _dirty.Add(scope);
        _scheduler.Schedule(this);
    }

    internal bool HasDirtyScopes => _dirty.Count > 0;

    /// <summary>Runs the before-update listeners, applies the hints and hands each dirty scope to its view.</summary>
    internal void Flush()
    {
        if (_released || _dirty.Count == 0)
        {
            return;
        }

        try
        {
            BeforeScopeUpdate?.Invoke();
        }
        catch (Exception caught)
        {
            _scheduler.Report("Computing derived state before a scope update", caught);
        }
        finally
        {
            UpdateCount = 0;
        }

        var batch = ApplyHints(_dirty);
        _dirty.Clear();

        // Root first, then the rest in the order they changed.
        batch.Sort((a, b) => ReferenceEquals(a, Root) ? -1 : ReferenceEquals(b, Root) ? 1 : 0);
        foreach (var scope in batch)
        {
            try
            {
                scope.BeginRender();
            }
            catch (Exception caught)
            {
                _scheduler.Report($"Rendering {scope.GetType().Name}", caught);
            }
        }
    }

    private List<Scope> ApplyHints(List<Scope> dirty)
    {
        var batch = new List<Scope>(dirty.Count);
        if (_hints.Count == 0)
        {
            batch.AddRange(dirty);
            return batch;
        }

        var collapsed = new HashSet<Type>();
        foreach (var (type, (_, max)) in _hints)
        {
            if (dirty.Count(s => s.GetType() == type) > max)
            {
                collapsed.Add(type);
            }
        }

        foreach (var scope in dirty)
        {
            if (collapsed.Contains(scope.GetType()))
            {
                scope.ResetRenderState(); // the fallback redraws it
            }
            else
            {
                batch.Add(scope);
            }
        }

        foreach (var type in collapsed)
        {
            var fallback = _hints[type].Fallback;
            if (!batch.Contains(fallback) && fallback.RequestRender())
            {
                batch.Add(fallback);
            }
        }

        return batch;
    }
}
