using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Wdc.Cube;

/// <summary>
/// The rendering state owned by a presenter (also called ViewState): what the view draws. Declare observed
/// properties with <see cref="Set{T}"/>:
/// <code>public string? Name { get; set => Set(ref field, value); }</code>
/// </summary>
/// <remarks>
/// <para>
/// Assigning a different value marks the scope dirty. Nothing is told to the view at once: the application flushes
/// dirty scopes in windows of about 16 ms, so a burst of changes becomes one redraw. A scope stays
/// <see cref="ScopeRenderState.Rendering"/> until its view calls <see cref="MarkRendered"/>; a change made meanwhile
/// is kept and goes out in the next window, never lost and never piled on a view that is still drawing.
/// </para>
/// <para>
/// <b>Equality.</b> Value types and strings compare by value; other objects by reference, as in the other Cube
/// versions. Replacing an object with an equal copy is a change: it tells the view the thing on screen is another one.
/// Changing <i>inside</i> an object already assigned is invisible to <see cref="Set{T}"/>: call
/// <see cref="MarkDirty"/>.
/// </para>
/// <para>
/// <b>Child scopes.</b> A scope assigned to an observed property, or added to a <see cref="ScopeList{T}"/>, that has no
/// owner yet is adopted: it reports to the same update manager. A scope that already belongs to another presenter
/// (an external scope, published through a slot) keeps its owner; this scope only holds the reference.
/// </para>
/// </remarks>
public abstract class Scope : INotifyPropertyChanged
{
    private List<Scope>? _awaitingAdoption;
    private HashSet<string>? _changedProperties;
    private bool _changedWhileRendering;
    private IScopeView? _view;
    private object? _viewToken;

    /// <summary>
    /// What makes this scope the same thing on screen across loads: a list reconciles its rows by it. Not observed.
    /// </summary>
    public object? Identity { get; set; }

    /// <summary>Where this scope is in the render cycle.</summary>
    public ScopeRenderState RenderState { get; private set; }

    /// <summary>The update manager this scope reports to; <see langword="null"/> until a presenter owns it.</summary>
    public ScopeUpdateManager? Manager { get; private set; }

    /// <summary>
    /// Raised during the flush, once per changed property, for renderers that bind property by property. Never
    /// raised from the setter itself.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Stores <paramref name="value"/> and marks the scope dirty when it differs from the current one.
    /// </summary>
    /// <returns>Whether the value changed.</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Same(field, value))
        {
            return false;
        }

        field = value;
        if (value is Scope child)
        {
            Adopt(child);
        }

        MarkDirty(propertyName);
        return true;
    }

    /// <summary>
    /// Marks the scope dirty by hand: for changes <see cref="Set{T}"/> cannot see (inside an object or collection
    /// already assigned).
    /// </summary>
    /// <exception cref="InvalidOperationException">Called outside the application's dispatcher.</exception>
    public void MarkDirty([CallerMemberName] string? propertyName = null)
    {
        var manager = Manager;
        if (manager is null)
        {
            return; // still being built: the owner marks it once it takes it
        }

        manager.VerifyAccess();
        if (propertyName is not null)
        {
            (_changedProperties ??= new HashSet<string>(StringComparer.Ordinal)).Add(propertyName);
        }

        switch (RenderState)
        {
            case ScopeRenderState.Clean:
                RenderState = ScopeRenderState.Dirty;
                manager.Enqueue(this);
                break;
            case ScopeRenderState.Rendering:
                _changedWhileRendering = true;
                break;
        }
    }

    /// <summary>
    /// Binds the view that draws this scope. A scope has one view; attaching another replaces it, and disposing a
    /// replaced binding does nothing.
    /// </summary>
    /// <returns>Disposing it detaches the view, if it is still the one attached.</returns>
    public IDisposable Attach(IScopeView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Manager?.VerifyAccess();

        var token = new object();
        _view = view;
        _viewToken = token;
        if (RenderState == ScopeRenderState.Rendering)
        {
            // The previous view never confirmed: let the new one draw what is pending.
            RenderState = ScopeRenderState.Clean;
            _changedWhileRendering = false;
            MarkDirty(null);
        }

        return new Binding(this, token);
    }

    /// <summary>
    /// Called by the view when it finished drawing what it was told. The scope becomes clean, or dirty again if it
    /// changed while the view was drawing. Safe to call from any thread.
    /// </summary>
    public void MarkRendered()
    {
        var manager = Manager;
        if (manager is not null && !manager.Dispatcher.CheckAccess())
        {
            manager.Dispatcher.Post(MarkRenderedCore);
            return;
        }

        MarkRenderedCore();
    }

    private void MarkRenderedCore()
    {
        if (RenderState != ScopeRenderState.Rendering)
        {
            return;
        }

        RenderState = ScopeRenderState.Clean;
        if (_changedWhileRendering)
        {
            _changedWhileRendering = false;
            MarkDirty(null);
        }
    }

    /// <summary>Takes <paramref name="child"/> under this scope's manager, now or as soon as this scope has one.</summary>
    protected internal void Adopt(Scope child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Manager is not null || ReferenceEquals(child, this))
        {
            return; // external scope, or already adopted
        }

        if (Manager is { } manager)
        {
            child.AdoptInto(manager);
        }
        else
        {
            (_awaitingAdoption ??= []).Add(child);
        }
    }

    internal void AdoptInto(ScopeUpdateManager manager)
    {
        if (Manager is not null)
        {
            return;
        }

        Manager = manager;
        var children = _awaitingAdoption;
        _awaitingAdoption = null;
        if (children is not null)
        {
            foreach (var child in children)
            {
                child.AdoptInto(manager);
            }
        }
    }

    /// <summary>Called by the flush: hands the changes to the view.</summary>
    internal void BeginRender()
    {
        if (RenderState != ScopeRenderState.Dirty)
        {
            return;
        }

        var changed = _changedProperties;
        _changedProperties = null;
        var view = _view;
        RenderState = view is null ? ScopeRenderState.Clean : ScopeRenderState.Rendering;

        if (changed is not null && PropertyChanged is { } handler)
        {
            foreach (var name in changed)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        view?.Invalidate(this);
    }

    /// <summary>
    /// Asks for a redraw on behalf of other scopes (a hint fallback) without going through the queue. Returns whether
    /// the scope should join the current batch; when it is still rendering, the redraw is kept for later instead.
    /// </summary>
    internal bool RequestRender()
    {
        switch (RenderState)
        {
            case ScopeRenderState.Clean:
                RenderState = ScopeRenderState.Dirty;
                return true;
            case ScopeRenderState.Rendering:
                _changedWhileRendering = true;
                return false;
            default:
                return true;
        }
    }

    /// <summary>Called when the owning manager is released: nothing is pending for this scope anymore.</summary>
    internal void ResetRenderState()
    {
        RenderState = ScopeRenderState.Clean;
        _changedWhileRendering = false;
        _changedProperties = null;
    }

    private void Detach(object token)
    {
        if (!ReferenceEquals(_viewToken, token))
        {
            return; // replaced by another view meanwhile
        }

        _view = null;
        _viewToken = null;
        if (RenderState == ScopeRenderState.Rendering)
        {
            RenderState = ScopeRenderState.Clean; // nobody left to confirm
            _changedWhileRendering = false;
        }
    }

    private static bool Same<T>(T current, T value)
    {
        if (typeof(T).IsValueType)
        {
            return EqualityComparer<T>.Default.Equals(current, value);
        }

        return current is string text
            ? string.Equals(text, value as string, StringComparison.Ordinal)
            : ReferenceEquals(current, value);
    }

    private sealed class Binding(Scope scope, object token) : IDisposable
    {
        private Scope? _scope = scope;

        public void Dispose()
        {
            var target = Interlocked.Exchange(ref _scope, null);
            if (target is null)
            {
                return;
            }

            if (target.Manager is { } manager && !manager.Dispatcher.CheckAccess())
            {
                manager.Dispatcher.Post(() => target.Detach(token));
            }
            else
            {
                target.Detach(token);
            }
        }
    }
}
