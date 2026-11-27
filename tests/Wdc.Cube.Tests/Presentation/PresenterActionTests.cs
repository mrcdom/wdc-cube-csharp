using Microsoft.Extensions.Time.Testing;
using Wdc.Cube.Testing;

namespace Wdc.Cube.Tests.Presentation;

/// <summary>
/// Guarded actions: they run on the dispatcher, never fault to the caller, flush at once (no window wait) and
/// republish the address. The fake clock is never advanced, so any render seen here came from the action itself.
/// </summary>
public sealed class PresenterActionTests
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly ActApp _app;
    private readonly FormPresenter _form;
    private readonly ScopeRenderRecorder _view = new();

    public PresenterActionTests()
    {
        _app = new ActApp(_dispatcher, new FakeTimeProvider());
        _dispatcher.Run(() => _app.Flip(ActPlaces.Form));
        _form = _app.PresenterOf<FormPresenter>(ActPlaces.Form);
        _dispatcher.Run(() =>
        {
            _view.Attach(_form.Scope);
            _app.Updates.FlushNow();
            return Task.CompletedTask;
        });
        _view.Clear();
    }

    [Fact]
    public void Action_RunsOnTheDispatcher_FlushesAtOnce_AndPublishesTheAddress()
    {
        _dispatcher.Run(() => _form.Scope.OnSave());

        Assert.True(_form.RanOnDispatcher);
        Assert.Equal(1, _form.Scope.Saved);
        Assert.Equal(1, _view.CountFor(_form.Scope)); // without waiting for the 16 ms window
        Assert.Equal("form?fs=1", _app.History.Token); // the action changed the identity of the state
    }

    [Fact]
    public async Task Action_CalledFromAnotherThread_IsMarshalled()
    {
        var pending = Task.Run(() => _form.Scope.OnSave(), TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        _dispatcher.Run(() => pending);

        Assert.True(_form.RanOnDispatcher);
    }

    [Fact]
    public void Action_Failure_GoesToUnexpected_NeverToTheCaller()
    {
        _dispatcher.Run(() => _form.Scope.OnFail()); // does not throw

        var (message, error) = Assert.Single(_app.UnexpectedErrors);
        Assert.Contains("OnFail", message, StringComparison.Ordinal); // the name comes from the binding site
        Assert.Equal("save failed", error.Message);
        Assert.Equal(1, _view.CountFor(_form.Scope)); // still redrawn: the screen reflects the state after failure
    }

    [Fact]
    public void Action_Failure_ThePresenterHandlesWhatItKnows_AndTheRestReachesTheApplication()
    {
        _form.Handles = caught => caught is TimeoutException;

        _dispatcher.Run(() => _form.Scope.OnTimeout());
        _dispatcher.Run(() => _form.Scope.OnFail());

        Assert.Equal([("OnTimeout", "service did not answer")], _form.Handled.Select(h => (h.Action, h.Error.Message)));
        Assert.Equal("Unavailable", _form.Scope.Name); // the screen's own notice
        var (message, error) = Assert.Single(_app.UnexpectedErrors);
        Assert.Contains("OnFail", message, StringComparison.Ordinal);
        Assert.Equal("save failed", error.Message);
    }

    [Fact]
    public void Action_Failure_AHandlerThatFails_LeavesBothToTheApplication()
    {
        _form.Handles = _ => throw new InvalidOperationException("handler broke");

        _dispatcher.Run(() => _form.Scope.OnFail());

        Assert.Equal(["save failed", "handler broke"], _app.UnexpectedErrors.Select(e => e.Error.Message));
    }

    [Fact]
    public void Action_Cancellation_IsAFailure_WhileThePresenterIsThere_AndNothingOnceItLeft()
    {
        _dispatcher.Run(() => _form.Scope.OnCancelled()); // a timeout of an HTTP call is a cancellation too

        Assert.IsType<OperationCanceledException>(Assert.Single(_app.UnexpectedErrors).Error);

        _app.UnexpectedErrors.Clear();
        _dispatcher.Run(() => _form.Scope.OnLeaveCancelled());

        Assert.True(_form.IsReleased);
        Assert.Empty(_app.UnexpectedErrors);
    }

    [Fact]
    public void Action_WithArgument()
    {
        _dispatcher.Run(() => _form.Scope.OnRename("Ana"));

        Assert.Equal("Ana", _form.Scope.Name);
        Assert.Equal(1, _view.CountFor(_form.Scope));
    }

    [Fact]
    public void AutoUpdate_RedrawsTheRootWhenTheActionMarkedNothing_AndCanBeTurnedOff()
    {
        _dispatcher.Run(() => _form.Scope.OnNothing());
        Assert.Equal(1, _view.CountFor(_form.Scope));

        _form.AutoUpdate = false;
        _view.Clear();
        _dispatcher.Run(() => _form.Scope.OnNothing());
        Assert.Equal(0, _view.CountFor(_form.Scope));
    }

    [Fact]
    public void UnboundAction_IsAHarmlessNoop_AndCanBeDetected()
    {
        Assert.True(ScopeActions.IsUnbound(_form.Scope.OnForgotten)); // the button that does nothing
        Assert.False(ScopeActions.IsUnbound(_form.Scope.OnSave));

        _dispatcher.Run(() => _form.Scope.OnForgotten()); // does not throw
    }

    [Fact]
    public void ActionOfAReleasedPresenter_DoesNothing()
    {
        var onSave = _form.Scope.OnSave;
        _dispatcher.Run(() => _app.Flip(ActPlaces.Other));
        Assert.True(_form.IsReleased);

        _dispatcher.Run(onSave); // a late click

        Assert.Equal(0, _form.Scope.Saved);
        Assert.Empty(_app.UnexpectedErrors);
    }

    [Fact]
    public void Actions_RunOneAtATime_EvenAcrossAwaits()
    {
        _form.Gate = new TaskCompletionSource();
        Task? first = null, second = null;
        _dispatcher.Run(() =>
        {
            first = _form.Scope.OnBuy();
            second = _form.Scope.OnBuy(); // the double click, while the first one waits on I/O
            return Task.CompletedTask;
        });

        Assert.Equal(1, _form.BuysStarted); // the second waits for the first to end
        _form.Gate.SetResult();
        _dispatcher.Run(() => Task.WhenAll(first!, second!));

        Assert.Equal(2, _form.BuysStarted);
        Assert.Equal(0, _form.Overlaps);
    }

    [Fact]
    public void ActionWaitingBehindOne_ThatLeftTheFace_DoesNothing()
    {
        _form.Gate = new TaskCompletionSource();
        _form.LeaveAfterBuy = true;
        Task? first = null, second = null;
        _dispatcher.Run(() =>
        {
            first = _form.Scope.OnBuy();
            second = _form.Scope.OnBuy();
            return Task.CompletedTask;
        });

        _form.Gate.SetResult();
        _dispatcher.Run(() => Task.WhenAll(first!, second!));

        Assert.Equal(1, _form.BuysStarted); // the first went elsewhere: the second click lands on a released presenter
        Assert.Empty(_app.UnexpectedErrors);
    }

    [Fact]
    public void ActionRunningAnother_RunsItAtOnce()
    {
        _dispatcher.Run(() => _form.Scope.OnSaveTwice()); // would wait for itself if the lock did not go along

        Assert.Equal(2, _form.Scope.Saved);
    }

    [Fact]
    public void ChildPresenter_HasItsOwnScopeAndManager_AndIsReleasedByItsOwner()
    {
        Assert.NotNull(_form.Child);
        Assert.Same(_form.Child.Scope, _form.Scope.Panel); // external scope: only the reference is the form's
        Assert.NotSame(_form.UpdateManager, _form.Child.UpdateManager);

        _dispatcher.Run(() => _app.Flip(ActPlaces.Other));

        Assert.True(_form.Child.IsReleased);
    }
}

// :: Fixture

internal static class ActPlaces
{
    public static readonly Place Form = Place.Create<ActApp>("form", a => new FormPresenter(a));
    public static readonly Place Other = Place.Create<ActApp>("other", a => new FormPresenter(a));
    public static readonly IReadOnlyList<Place> All = [Form, Other];
}

internal sealed class ActApp(PresentationDispatcher dispatcher, TimeProvider time)
    : CubeApplication(new TestHistoryManager(), ActPlaces.All, dispatcher: dispatcher, timeProvider: time)
{
    public new TestHistoryManager History => (TestHistoryManager)base.History;

    public List<(string Message, Exception Error)> UnexpectedErrors { get; } = [];

    public override void Unexpected(string message, Exception error) => UnexpectedErrors.Add((message, error));
}

internal sealed class FormScope : Scope
{
    public string? Name { get; set => Set(ref field, value); }

    public int Saved { get; set => Set(ref field, value); }

    public PanelScope? Panel { get; set => Set(ref field, value); }

    public Func<Task> OnSave { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnFail { get; set; } = ScopeActions.Unbound();

    public Func<string, Task> OnRename { get; set; } = ScopeActions.Unbound<string>();

    public Func<Task> OnNothing { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnForgotten { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnBuy { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnSaveTwice { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnTimeout { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnCancelled { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnLeaveCancelled { get; set; } = ScopeActions.Unbound();
}

internal sealed class PanelScope : Scope
{
    public string? Text { get; set => Set(ref field, value); }
}

/// <summary>A child presenter: no place, created and released by its owner.</summary>
internal sealed class PanelPresenter(ActApp app) : Presenter<ActApp, PanelScope>(app, new PanelScope());

internal sealed class FormPresenter(ActApp app) : CubePresenter<ActApp, FormScope>(app, new FormScope())
{
    private int _saves;

    public bool RanOnDispatcher { get; private set; }

    public PanelPresenter? Child { get; private set; }

    public TaskCompletionSource? Gate { get; set; }

    public bool LeaveAfterBuy { get; set; }

    public int BuysStarted { get; private set; }

    public int Overlaps { get; private set; }

    private bool _buying;

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        if (initialization)
        {
            Scope.OnSave = Action(OnSave);
            Scope.OnFail = Action(OnFail);
            Scope.OnRename = Action<string>(OnRename);
            Scope.OnNothing = Action(OnNothing);
            Scope.OnBuy = Action(OnBuy);
            Scope.OnSaveTwice = Action(OnSaveTwice);
            Scope.OnTimeout = Action(OnTimeout);
            Scope.OnCancelled = Action(OnCancelled);
            Scope.OnLeaveCancelled = Action(OnLeaveCancelled);
            Child = new PanelPresenter(App);
            Scope.Panel = Child.Scope;
        }

        _saves = (int)intent.GetInt64("fs", _saves);
        return Task.FromResult(true);
    }

    public override void PublishParameters(FlipIntent intent) => intent.SetParameter("fs", _saves == 0 ? null : _saves);

    public override void Release()
    {
        Child?.Release();
        base.Release();
    }

    private async Task OnSave()
    {
        RanOnDispatcher = App.Dispatcher.CheckAccess();
        await Task.Yield();
        RanOnDispatcher &= App.Dispatcher.CheckAccess();
        Scope.Saved++;
        _saves++;
    }

    private async Task OnBuy()
    {
        BuysStarted++;
        Overlaps += _buying ? 1 : 0;
        _buying = true;
        await (Gate?.Task ?? Task.CompletedTask);
        _buying = false;
        if (LeaveAfterBuy)
        {
            await App.Flip(ActPlaces.Other);
        }
    }

    private async Task OnSaveTwice()
    {
        await Scope.OnSave();
        await Scope.OnSave();
    }

    private Task OnFail() => throw new InvalidOperationException("save failed");

    private Task OnTimeout() => throw new TimeoutException("service did not answer");

    private Task OnCancelled() => throw new OperationCanceledException();

    private async Task OnLeaveCancelled()
    {
        await App.Flip(ActPlaces.Other);
        throw new OperationCanceledException();
    }

    /// <summary>Which failures this screen shows by itself; the others go on to the application.</summary>
    public Func<Exception, bool>? Handles { get; set; }

    public List<(string Action, Exception Error)> Handled { get; } = [];

    protected override void OnActionFailure(string action, Exception caught)
    {
        if (Handles?.Invoke(caught) == true)
        {
            Handled.Add((action, caught));
            Scope.Name = "Unavailable";
            return;
        }

        base.OnActionFailure(action, caught);
    }

    private void OnRename(string name) => Scope.Name = name;

    private void OnNothing()
    {
    }
}
