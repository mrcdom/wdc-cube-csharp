namespace Wdc.Cube.Testing;

/// <summary>
/// A view that only counts: attach it to scopes to prove how many redraws a change caused ("changing one row redrew
/// one row"). It confirms each render at once, unless <see cref="AutoConfirm"/> is off, so a test can hold a scope in
/// <see cref="ScopeRenderState.Rendering"/>.
/// </summary>
public sealed class ScopeRenderRecorder : IScopeView
{
    private readonly Dictionary<Scope, int> _counts = new(ReferenceEqualityComparer.Instance);
    private readonly List<IDisposable> _bindings = [];

    /// <summary>Whether each render is confirmed as soon as it is requested.</summary>
    public bool AutoConfirm { get; set; } = true;

    /// <summary>Every render request, in order.</summary>
    public List<Scope> Renders { get; } = [];

    /// <summary>Attaches this recorder as the view of each scope.</summary>
    public ScopeRenderRecorder Attach(params Scope[] scopes)
    {
        foreach (var scope in scopes)
        {
            _bindings.Add(scope.Attach(this));
        }

        return this;
    }

    /// <summary>How many times <paramref name="scope"/> was told to redraw.</summary>
    public int CountFor(Scope scope) => _counts.GetValueOrDefault(scope);

    /// <summary>Forgets the counts.</summary>
    public void Clear()
    {
        _counts.Clear();
        Renders.Clear();
    }

    /// <summary>Detaches from every scope.</summary>
    public void Detach()
    {
        foreach (var binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
    }

    /// <inheritdoc/>
    public void Invalidate(Scope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _counts[scope] = CountFor(scope) + 1;
        Renders.Add(scope);
        if (AutoConfirm)
        {
            scope.MarkRendered();
        }
    }
}
