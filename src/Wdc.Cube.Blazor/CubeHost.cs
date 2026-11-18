using Microsoft.AspNetCore.Components;

namespace Wdc.Cube.Blazor;

/// <summary>What <see cref="CubeHost{TApp}"/> hands to the application it creates.</summary>
/// <param name="Dispatcher">The renderer's serial context, as a presentation dispatcher.</param>
/// <param name="History">The browser's address.</param>
public sealed record CubeHostContext(PresentationDispatcher Dispatcher, HistoryManager History);

/// <summary>
/// Creates one application per mounted host (per circuit in Blazor Server, per page in WebAssembly) on the
/// renderer's dispatcher, starts it, draws it and releases it when the host goes away.
/// </summary>
/// <example>
/// <code>
/// &lt;CubeHost TApp="MainPresenter"
///           CreateApplication="ctx =&gt; new MainPresenter(ctx.History, ctx.Dispatcher)"
///           Start="app =&gt; app.KickStart(Places.Main)"&gt;
///     &lt;ViewSlot Scope="context.Scope" /&gt;
/// &lt;/CubeHost&gt;
/// </code>
/// </example>
public sealed class CubeHost<TApp> : IComponent, IAsyncDisposable
    where TApp : CubeApplication
{
    private RenderHandle _renderHandle;
    private NavigationHistoryManager? _history;
    private TApp? _app;

    /// <summary>Creates the application.</summary>
    [Parameter]
    [EditorRequired]
    public Func<CubeHostContext, TApp> CreateApplication { get; set; } = null!;

    /// <summary>Starts the application (usually <c>KickStart</c>).</summary>
    [Parameter]
    public Func<TApp, Task>? Start { get; set; }

    /// <summary>What to draw, given the application.</summary>
    [Parameter]
    public RenderFragment<TApp>? ChildContent { get; set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>The application, once created.</summary>
    public TApp? Application => _app;

    /// <inheritdoc/>
    public void Attach(RenderHandle renderHandle) => _renderHandle = renderHandle;

    /// <inheritdoc/>
    public async Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        if (_app is not null)
        {
            Render();
            return;
        }

        _history = new NavigationHistoryManager(Navigation);
        _app = CreateApplication(new CubeHostContext(BlazorPresentation.CreateDispatcher(_renderHandle.Dispatcher), _history));
        Render();
        if (Start is not null)
        {
            await Start(_app);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_app is { } app && !app.IsReleased)
        {
            await app.InvokeAsync(app.Release);
        }

        _history?.Dispose();
    }

    private void Render()
    {
        if (_app is { } app && ChildContent is { } content)
        {
            _renderHandle.Render(content(app));
        }
    }
}
