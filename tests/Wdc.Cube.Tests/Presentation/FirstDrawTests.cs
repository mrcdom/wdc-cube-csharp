using Microsoft.Extensions.Time.Testing;
using Wdc.Cube.Testing;

namespace Wdc.Cube.Tests.Presentation;

/// <summary>Every presenter asks for the first draw of its scope, wherever it is built.</summary>
public sealed class FirstDrawTests
{
    private readonly ManualDispatcher _dispatcher = new();

    [Fact]
    public void AnApplicationBuiltOffTheDispatcher_AsksForItsFirstDrawThere()
    {
        var app = new DrawApp(_dispatcher);
        Assert.Equal(ScopeRenderState.Clean, app.Scope.RenderState); // nothing ran yet

        _dispatcher.RunPending();

        Assert.Equal(ScopeRenderState.Dirty, app.Scope.RenderState);
        Assert.Equal(1, app.UpdateManager.UpdateCount);
    }

    [Fact]
    public void AnApplicationBuiltOnTheDispatcher_IsDirtyAtOnce()
    {
        var app = _dispatcher.Run(() => Task.FromResult(new DrawApp(_dispatcher)));

        Assert.Equal(1, app.UpdateManager.UpdateCount);
    }

    [Fact]
    public void APresenterBuiltOffTheDispatcher_AsksForItsFirstDrawThere()
    {
        var app = _dispatcher.Run(() => Task.FromResult(new DrawApp(_dispatcher)));
        var presenter = new DrawPresenter(app);

        _dispatcher.RunPending();

        Assert.Equal(ScopeRenderState.Dirty, presenter.Scope.RenderState);
    }

    private sealed class DrawScope : Scope;

    private sealed class DrawApp(ManualDispatcher dispatcher)
        : ApplicationPresenter<DrawScope>(new DrawScope(), new TestHistoryManager(), [], dispatcher: dispatcher,
            timeProvider: new FakeTimeProvider());

    private sealed class DrawPresenter(DrawApp app) : Presenter<DrawApp, DrawScope>(app, new DrawScope());
}
