using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using Wdc.Cube.Testing;

namespace Wdc.Cube.Tests.Presentation;

/// <summary>
/// Scope observation and the update pipeline: changes are gathered in a ~16 ms window, a scope is
/// clean only after its view confirms the render, and nothing changed meanwhile is lost.
/// </summary>
public sealed class UpdatePipelineTests
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new();
    private readonly List<Exception> _errors = [];
    private readonly UpdateScheduler _scheduler;

    public UpdatePipelineTests()
    {
        _scheduler = new UpdateScheduler(_dispatcher, _time, report: (_, e) => _errors.Add(e));
    }

    private void On(Action action) => _dispatcher.Run(() =>
    {
        action();
        return Task.CompletedTask;
    });

    /// <summary>Lets <paramref name="ms"/> pass and runs whatever the timer posted.</summary>
    private void Tick(int ms = 16)
    {
        _time.Advance(TimeSpan.FromMilliseconds(ms));
        _dispatcher.RunPending();
    }

    private ScopeUpdateManager Own(Scope root) => new(_scheduler, root);

    /// <summary>Attaches the recorder on the dispatcher, where views are bound.</summary>
    private ScopeRenderRecorder Attached(ScopeRenderRecorder recorder, params Scope[] scopes)
    {
        On(() => recorder.Attach(scopes));
        return recorder;
    }

    [Fact]
    public void Set_MarksDirtyOnlyWhenTheValueChanges()
    {
        var scope = new OrderScope();
        Own(scope);

        On(() =>
        {
            scope.Count = 0;
            Assert.Equal(ScopeRenderState.Clean, scope.RenderState);

            scope.Title = "a";
            Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);
        });
    }

    [Fact]
    public void Equality_ByValue_CollectionsByContent_ScopesByReference()
    {
        var scope = new OrderScope();
        Own(scope);
        On(() => scope.Title = "same");
        Tick();

        On(() => scope.Title = new string("same".AsSpan())); // another instance, same text
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);

        On(() => scope.Tag = new Money(10));
        Tick();
        On(() => scope.Tag = new Money(10)); // an equal record
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);

        On(() => scope.Tag = new List<Money> { new(1), new(2) });
        Tick();
        On(() => scope.Tag = new[] { new Money(1), new Money(2) }); // another collection, same items in order
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
        On(() => scope.Tag = new[] { new Money(2), new Money(1) });
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);
        Tick();

        On(() => scope.Tag = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        Tick();
        On(() => scope.Tag = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }); // same entries
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
        On(() => scope.Tag = new Dictionary<string, int> { ["a"] = 1, ["b"] = 3 });
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);
        Tick();

        On(() => scope.Tag = new object());
        Tick();
        On(() => scope.Tag = new object()); // a class without value equality: another thing
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);
        Tick();

        var external = new DetailScope();
        On(() => scope.External = external);
        Tick();
        On(() => scope.External = new DetailScope()); // another scope is another view, whatever it holds
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);
    }

    [Fact]
    public void ABurstOfChanges_ReachesTheViewOnce_AfterTheWindow()
    {
        var scope = new OrderScope();
        Own(scope);
        var view = Attached(new ScopeRenderRecorder(), scope);

        On(() =>
        {
            for (var i = 1; i <= 100; i++)
            {
                scope.Count = i;
            }
        });

        Tick(15);
        Assert.Equal(0, view.CountFor(scope)); // not before the window
        Tick(1);
        Assert.Equal(1, view.CountFor(scope));
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }

    [Fact]
    public void Scope_IsCleanOnlyAfterTheViewConfirms_AndChangesWhileRenderingAreNotLost()
    {
        var scope = new OrderScope();
        Own(scope);
        var view = Attached(new ScopeRenderRecorder { AutoConfirm = false }, scope);

        On(() => scope.Title = "first");
        Tick();
        Assert.Equal(ScopeRenderState.Rendering, scope.RenderState);
        Assert.Equal(1, view.CountFor(scope));

        On(() => scope.Title = "second"); // the view is still drawing "first"
        Tick();
        Tick();
        Assert.Equal(1, view.CountFor(scope)); // not piled on a view that has not finished
        Assert.Equal(ScopeRenderState.Rendering, scope.RenderState);

        On(scope.MarkRendered);
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState); // "second" is still owed
        Tick();
        Assert.Equal(2, view.CountFor(scope));

        On(scope.MarkRendered);
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }

    [Fact]
    public void WithoutAView_TheFlushLeavesTheScopeClean()
    {
        var scope = new OrderScope();
        Own(scope);

        On(() => scope.Title = "x");
        Tick();

        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }

    [Fact]
    public void ChildScopes_CreatedBeforeTheOwner_AreAdoptedAndRedrawOnTheirOwn()
    {
        var scope = new OrderScope(); // creates Detail in its constructor, before any manager exists
        Own(scope);
        var view = Attached(new ScopeRenderRecorder(), scope, scope.Detail!);

        On(() => scope.Detail!.Text = "changed");
        Tick();

        Assert.Same(scope.Manager, scope.Detail!.Manager);
        Assert.Equal(1, view.CountFor(scope.Detail));
        Assert.Equal(0, view.CountFor(scope)); // the parent did not change
    }

    [Fact]
    public void ChildScopes_AssignedLater_AreAdoptedToo()
    {
        var scope = new OrderScope();
        Own(scope);
        var fresh = new DetailScope();

        On(() => scope.Detail = fresh);

        Assert.Same(scope.Manager, fresh.Manager);
    }

    [Fact]
    public void ExternalScope_KeepsItsOwner_AndOnlyTheReferenceBelongsToTheHolder()
    {
        var holder = new OrderScope();
        var other = new DetailScope();
        var holderManager = Own(holder);
        var otherManager = Own(other);
        var view = Attached(new ScopeRenderRecorder(), holder, other);

        On(() => holder.External = other);
        Tick();
        Assert.Same(otherManager, other.Manager); // not adopted
        Assert.Equal(1, view.CountFor(holder));

        view.Clear();
        On(() => other.Text = "external change");
        Tick();
        Assert.Equal(1, view.CountFor(other));
        Assert.Equal(0, view.CountFor(holder));
        Assert.NotSame(holderManager, otherManager);
    }

    [Fact]
    public void BeforeScopeUpdate_SeesAllChangesOnce_AndItsChangesGoOutInTheSameFlush()
    {
        var scope = new OrderScope();
        var manager = Own(scope);
        var calls = 0;
        manager.BeforeScopeUpdate += () =>
        {
            calls++;
            scope.Total = scope.Count * 2; // derived state, computed in one place
        };
        var view = Attached(new ScopeRenderRecorder(), scope);

        On(() =>
        {
            scope.Count = 1;
            scope.Count = 21;
        });
        Tick();

        Assert.Equal(1, calls);
        Assert.Equal(42, scope.Total);
        Assert.Equal(1, view.CountFor(scope));
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public void Hint_ManyDirtyRows_RedrawTheListInstead(int changedRows, bool collapsed)
    {
        var scope = new OrderScope();
        var manager = Own(scope);
        manager.Hint<RowScope>(scope, maxUpdates: 10);
        var rows = Enumerable.Range(0, 20).Select(i => new RowScope { Label = $"r{i}" }).ToArray();
        On(() => scope.Rows.Assign(rows));
        Tick();
        var view = Attached(new ScopeRenderRecorder(), [scope, .. rows]);

        On(() =>
        {
            for (var i = 0; i < changedRows; i++)
            {
                rows[i].Label += "!";
            }
        });
        Tick();

        Assert.Equal(collapsed ? 1 : 0, view.CountFor(scope));
        Assert.Equal(collapsed ? 0 : changedRows, rows.Sum(view.CountFor));
        Assert.All(rows, r => Assert.Equal(ScopeRenderState.Clean, r.RenderState));
    }

    [Fact]
    public void ScopeList_StructuralChangesMarkTheOwner_SameInstancesDoNot()
    {
        var scope = new OrderScope();
        Own(scope);
        var a = new RowScope();
        var b = new RowScope();

        On(() => scope.Rows.Assign([a, b]));
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);
        Assert.Same(scope.Manager, a.Manager); // items adopted
        Tick();

        On(() => scope.Rows.Assign([a, b])); // same instances, same order: nothing to redraw
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);

        On(() => scope.Rows.Set(0, a));
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);

        On(() => scope.Rows.Assign([b, a]));
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);
        Tick();

        On(() => scope.Rows.RemoveAll(_ => false));
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
        On(() => scope.Rows.Remove(a));
        Assert.Equal([b], scope.Rows);
        Assert.Equal(ScopeRenderState.Dirty, scope.RenderState);
    }

    [Fact]
    public void ScopeList_CollectionChanged_ComesAtTheFlush_AsAReadOnlyList()
    {
        var scope = new OrderScope();
        Own(scope);
        var resets = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)scope.Rows).CollectionChanged += (_, e) =>
        {
            Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, e.Action);
            resets++;
        };

        On(() =>
        {
            scope.Rows.Add(new RowScope());
            scope.Rows.Add(new RowScope());
        });
        Assert.Equal(0, resets); // not by the mutation...

        Tick();
        Assert.Equal(1, resets); // ...once, with the batch

        var asList = (System.Collections.IList)scope.Rows; // what binding renderers read
        Assert.True(asList.IsReadOnly);
        Assert.Equal(2, asList.Count);
        Assert.Same(scope.Rows[1], asList[1]);
        Assert.Throws<NotSupportedException>(() => asList.Add(new RowScope()));
    }

    [Fact]
    public void PropertyChanged_IsRaisedAtTheFlush_NotByTheSetter()
    {
        var scope = new OrderScope();
        Own(scope);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)scope).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        On(() =>
        {
            scope.Title = "a";
            scope.Title = "b";
            scope.Count = 3;
            scope.Rows.Add(new RowScope());
        });
        Assert.Empty(raised);

        Tick();
        Assert.Equal(["Title", "Count", "Rows"], raised);
    }

    [Fact]
    public void ChangingAnOwnedScopeOutsideTheDispatcher_Fails_ButBuildingItBeforeIsFine()
    {
        var scope = new OrderScope { Title = "while building" }; // no owner yet: allowed anywhere
        Own(scope);

        Assert.Throws<InvalidOperationException>(() => scope.Title = "from a foreign thread");
    }

    [Fact]
    public async Task MarkRendered_FromAnotherThread_IsMarshalled()
    {
        var scope = new OrderScope();
        Own(scope);
        Attached(new ScopeRenderRecorder { AutoConfirm = false }, scope);
        On(() => scope.Title = "x");
        Tick();

        await Task.Run(scope.MarkRendered, TestContext.Current.CancellationToken);
        Assert.Equal(ScopeRenderState.Rendering, scope.RenderState); // not yet: it was posted
        _dispatcher.RunPending();
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }

    [Fact]
    public void ReplacingTheView_OldBindingDisposeIsHarmless_DetachingWhileRenderingCleans()
    {
        var scope = new OrderScope();
        Own(scope);
        var first = new ScopeRenderRecorder { AutoConfirm = false };
        var second = new ScopeRenderRecorder { AutoConfirm = false };

        IDisposable firstBinding = null!;
        IDisposable secondBinding = null!;
        On(() => firstBinding = scope.Attach(first));
        On(() => scope.Title = "x");
        Tick();
        Assert.Equal(1, first.CountFor(scope));

        // A new view replaces the one that never confirmed: it gets what is pending.
        On(() => secondBinding = scope.Attach(second));
        Tick();
        Assert.Equal(1, second.CountFor(scope));

        On(firstBinding.Dispose); // replaced: does nothing
        Assert.Equal(ScopeRenderState.Rendering, scope.RenderState);

        On(secondBinding.Dispose); // nobody left to confirm
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }

    [Fact]
    public void ViewsThatKeepChangingScopes_AreReportedAsALoop_AndDoNotHangTheDispatcher()
    {
        var a = new OrderScope();
        var b = new OrderScope();
        Own(a);
        Own(b);
        On(() =>
        {
            a.Attach(ScopeViews.Immediate(_ => b.Count++));
            b.Attach(ScopeViews.Immediate(_ => a.Count++));
        });

        On(() => a.Count = 1);
        Tick();

        Assert.Single(_errors);
        Assert.True(_scheduler.HasPendingUpdates); // the rest waits for the next window instead of spinning
    }

    [Fact]
    public void ReleasedManager_DropsWhatIsPending_AndIgnoresLaterChanges()
    {
        var scope = new OrderScope();
        var manager = Own(scope);
        var view = Attached(new ScopeRenderRecorder(), scope);
        On(() => scope.Title = "pending");

        manager.Release();
        Tick();
        On(() => scope.Title = "after release");
        Tick();

        Assert.Equal(0, view.CountFor(scope));
        Assert.False(_scheduler.HasPendingUpdates);
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }

    [Fact]
    public async Task Settle_FlushesWithoutWaitingForTheWindow()
    {
        var app = new CubeApplication(new TestHistoryManager(), []);
        var scope = new OrderScope();
        _ = new ScopeUpdateManager(app.Updates, scope);
        var view = new ScopeRenderRecorder();
        await app.InvokeAsync(() =>
        {
            view.Attach(scope);
            scope.Title = "x";
        });

        await app.Settle();

        Assert.Equal(1, view.CountFor(scope));
        Assert.Equal(ScopeRenderState.Clean, scope.RenderState);
    }
}

internal sealed record Money(decimal Amount);

internal sealed class OrderScope : Scope
{
    public OrderScope()
    {
        Rows = new ScopeList<RowScope>(this, nameof(Rows));
        Detail = new DetailScope();
    }

    public string? Title { get; set => Set(ref field, value); }

    public int Count { get; set => Set(ref field, value); }

    public int Total { get; set => Set(ref field, value); }

    public object? Tag { get; set => Set(ref field, value); }

    public DetailScope? Detail { get; set => Set(ref field, value); }

    public Scope? External { get; set => Set(ref field, value); }

    public ScopeList<RowScope> Rows { get; }
}

internal sealed class DetailScope : Scope
{
    public string? Text { get; set => Set(ref field, value); }
}

internal sealed class RowScope : Scope
{
    public string? Label { get; set => Set(ref field, value); }
}
