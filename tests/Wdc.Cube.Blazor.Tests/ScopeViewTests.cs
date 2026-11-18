using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Time.Testing;

namespace Wdc.Cube.Blazor.Tests;

/// <summary>
/// The Blazor bridge, on Blazor's real renderer: a scope change reaches the component only after the update window,
/// the component confirms the render, and the slot keys views by scope instance.
/// </summary>
public sealed class ScopeViewTests : IDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly FakeTimeProvider _time = new();
    private readonly UpdateScheduler _scheduler;

    public ScopeViewTests()
    {
        _ctx.Services.AddCubeViews(r => r.Register<LabelScope, LabelView>());
        _scheduler = new UpdateScheduler(BlazorPresentation.CreateDispatcher(_ctx.Renderer.Dispatcher), _time);
    }

    public void Dispose() => _ctx.Dispose();

    private LabelScope Owned(string text)
    {
        var scope = new LabelScope { Text = text };
        _ = new ScopeUpdateManager(_scheduler, scope);
        return scope;
    }

    private Task OnRenderer(Action action) => _ctx.Renderer.Dispatcher.InvokeAsync(action);

    [Fact]
    public async Task ScopeChange_RedrawsAfterTheWindow_AndTheScopeIsCleanOnlyAfterRendering()
    {
        var scope = Owned("first");
        var cut = _ctx.Render<ViewSlot>(p => p.Add(x => x.Scope, scope));
        Assert.Equal("first", cut.Find("span.label").TextContent);

        await OnRenderer(() => scope.Text = "second");
        Assert.Equal("first", cut.Find("span.label").TextContent); // still inside the window
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);

        _time.Advance(UpdateScheduler.DefaultWindow);
        cut.WaitForAssertion(() => Assert.Equal("second", cut.Find("span.label").TextContent));
        cut.WaitForAssertion(() => Assert.Equal(ScopeRenderState.Clean, scope.RenderState)); // confirmed by OnAfterRender
    }

    [Fact]
    public async Task ABurstOfChanges_IsOneRender()
    {
        var scope = Owned("0");
        var cut = _ctx.Render<ViewSlot>(p => p.Add(x => x.Scope, scope));
        var view = cut.FindComponent<LabelView>().Instance;
        var before = view.Renders;

        await OnRenderer(() =>
        {
            for (var i = 1; i <= 50; i++)
            {
                scope.Text = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        });
        _time.Advance(UpdateScheduler.DefaultWindow);

        cut.WaitForAssertion(() => Assert.Equal("50", cut.Find("span.label").TextContent));
        Assert.Equal(before + 1, view.Renders);
    }

    [Fact]
    public async Task Slot_KeepsTheViewForTheSameScope_AndReplacesItForAnotherScope()
    {
        var first = Owned("a");
        var cut = _ctx.Render<ViewSlot>(p => p.Add(x => x.Scope, first));
        var firstView = cut.FindComponent<LabelView>().Instance;

        await OnRenderer(() =>
        {
            first.Text = "b";
            _scheduler.FlushNow();
        });
        cut.WaitForAssertion(() => Assert.Equal("b", cut.Find("span.label").TextContent));
        Assert.Same(firstView, cut.FindComponent<LabelView>().Instance); // updated in place

        var second = Owned("c");
        cut.Render(p => p.Add(x => x.Scope, second));
        Assert.NotSame(firstView, cut.FindComponent<LabelView>().Instance); // another thing on screen
        Assert.Equal("c", cut.Find("span.label").TextContent);
    }

    [Fact]
    public void Slot_WithoutARegisteredView_SaysSoOnScreen()
    {
        var cut = _ctx.Render<ViewSlot>(p => p.Add(x => x.Scope, new UnknownScope()));

        Assert.Contains("No view registered for UnknownScope", cut.Find(".cube-missing-view").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Slot_Empty_DrawsTheEmptyFragment()
    {
        var cut = _ctx.Render<ViewSlot>(p => p
            .Add(x => x.Scope, null)
            .Add(x => x.Empty, (RenderFragment)(b => b.AddContent(0, "nothing here"))));

        Assert.Equal("nothing here", cut.Markup);
    }

    [Fact]
    public async Task RemovingTheView_WhileItOwesARender_LeavesTheScopeClean()
    {
        var scope = Owned("x");
        var cut = _ctx.Render<ViewSlot>(p => p.Add(x => x.Scope, scope));
        var view = cut.FindComponent<LabelView>().Instance;
        view.HoldRenders = true;

        await OnRenderer(() =>
        {
            scope.Text = "y";
            _scheduler.FlushNow();
        });
        cut.WaitForAssertion(() => Assert.Equal(ScopeRenderState.Rendering, scope.RenderState));

        cut.Render(p => p.Add(x => x.Scope, null)); // the view goes away without confirming
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }
}

internal sealed class LabelScope : Scope
{
    public string? Text { get; set => Set(ref field, value); }
}

internal sealed class UnknownScope : Scope;

internal sealed class LabelView : ScopeView<LabelScope>
{
    public int Renders { get; private set; }

    /// <summary>Simulates a view that never gets to draw what it was told (it is about to be removed).</summary>
    public bool HoldRenders { get; set; }

    protected override bool ShouldRender() => !HoldRenders;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Renders++;
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "label");
        builder.AddContent(2, Scope.Text);
        builder.CloseElement();
    }
}
