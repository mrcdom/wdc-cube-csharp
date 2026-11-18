using Tutorial.Presentation.Modules.TodoMvc;
using Wdc.Cube.Testing;

namespace Tutorial.Presentation.Tests;

/// <summary>Ported from <c>presentation-test/src/todo-mvc.test.ts</c>, plus row reconciliation.</summary>
public sealed class TodoMvcTests
{
    private static TodoMvcScope Todo(Harness h) => Assert.IsType<TodoMvcScope>(h.Scope.Body);

    private static IReadOnlyList<ItemScope> Rows(Harness h) => Todo(h).List?.Items ?? (IReadOnlyList<ItemScope>)[];

    private static async Task Type(Harness h, string text)
    {
        await h.Do(() => Todo(h).Header!.OnInputChange(text));
        await h.Do(() => Todo(h).Header!.OnInputKeyDown("Enter"));
    }

    [Fact]
    public async Task StartsOnTheSampleList_CountingOnlyWhatIsActive()
    {
        var h = await Harness.Start("todos");

        Assert.Equal(5, Rows(h).Count);
        Assert.Equal(2, Todo(h).Footer!.Count);
        Assert.Equal("items", Todo(h).Footer!.ActiveTodoWord);
        Assert.True(Todo(h).Header!.ToggleButtonVisible);
    }

    [Fact]
    public async Task EveryItemHasAnIdentityThatFollowsTheData()
    {
        var h = await Harness.Start("todos");

        Assert.Equal([1L, 2L, 3L, 4L, 5L], Rows(h).Select(r => (long)r.Identity!));
    }

    [Fact]
    public async Task AddsWhatWasTyped_ClearsTheField_AndGivesItAnIdentity()
    {
        var h = await Harness.Start("todos");

        await Type(h, "  Buy milk  ");

        var added = Rows(h)[^1];
        Assert.Equal("Buy milk", added.Title);
        Assert.Equal(6L, added.Identity);
        Assert.Equal(string.Empty, Todo(h).Header!.InputValue);
        Assert.Equal(3, Todo(h).Footer!.Count);
    }

    [Fact]
    public async Task IgnoresAnEmptyEntry()
    {
        var h = await Harness.Start("todos");

        await Type(h, "   ");

        Assert.Equal(5, Rows(h).Count);
    }

    [Fact]
    public async Task Toggle_MovesAnItemBetweenActiveAndCompleted()
    {
        var h = await Harness.Start("todos");
        var walk = Rows(h)[0];

        await h.Do(walk.OnToggle);

        Assert.True(walk.Completed);
        Assert.Equal(1, Todo(h).Footer!.Count);
        Assert.Equal("item", Todo(h).Footer!.ActiveTodoWord);
    }

    [Fact]
    public async Task Destroy_RemovesTheItemAskedFor_AndLeavesTheRest()
    {
        var h = await Harness.Start("todos");
        var school = Rows(h)[2];

        await h.Do(school.OnDestroy);

        Assert.Equal([1L, 2L, 4L, 5L], Rows(h).Select(r => r.Id));
    }

    [Fact]
    public async Task ClearCompleted_KeepsTheActiveOnes_AndHidesTheButton()
    {
        var h = await Harness.Start("todos");
        Assert.True(Todo(h).Footer!.ClearButtonVisible);

        await h.Do(Todo(h).Footer!.OnClearCompleted);

        Assert.Equal([1L, 3L], Rows(h).Select(r => r.Id));
        Assert.False(Todo(h).Footer!.ClearButtonVisible);
    }

    [Fact]
    public async Task Filter_NarrowsTheList_AndSaysSoInTheAddress()
    {
        var h = await Harness.Start("todos");

        await h.Do(Todo(h).Footer!.OnShowActives);
        Assert.Equal([1L, 3L], Rows(h).Select(r => r.Id));
        Assert.Equal("todos?tm-showing=1", h.History.Token);

        await h.Do(Todo(h).Footer!.OnShowCompleteds);
        Assert.Equal([2L, 4L, 5L], Rows(h).Select(r => r.Id));

        await h.Do(Todo(h).Footer!.OnShowAll);
        Assert.Equal(5, Rows(h).Count);
        Assert.Equal("todos", h.History.Token); // the default stays out of the address
    }

    [Fact]
    public async Task Filter_IsRestoredFromTheAddress()
    {
        var h = await Harness.Start("todos?tm-showing=2");

        Assert.Equal(ShowingOptions.Completed, Todo(h).Footer!.Showing);
        Assert.Equal([2L, 4L, 5L], Rows(h).Select(r => r.Id));
    }

    [Fact]
    public async Task Editing_CommitsTheNewTitleOnEnter()
    {
        var h = await Harness.Start("todos");
        var row = Rows(h)[0];

        await h.Do(row.OnEdit);
        Assert.True(row.Editing);
        await h.Do(() => row.OnKeyDown(" Walk the cat ", "Enter"));

        Assert.Equal("Walk the cat", row.Title);
        Assert.False(row.Editing);
    }

    [Fact]
    public async Task Editing_EscapeDiscards_EvenWhenTheClosingBoxFiresBlur()
    {
        var h = await Harness.Start("todos");
        var row = Rows(h)[0];
        var title = row.Title;

        await h.Do(row.OnEdit);
        await h.Do(() => row.OnKeyDown("Discarded", "Escape"));
        await h.Do(() => row.OnBlur("Discarded")); // the browser blurs the box that leaves the screen

        Assert.Equal(title, row.Title);
        Assert.False(row.Editing);
    }

    [Fact]
    public async Task Editing_DropsTheItemWhenTheNewTitleIsBlank()
    {
        var h = await Harness.Start("todos");
        var row = Rows(h)[0];

        await h.Do(row.OnEdit);
        await h.Do(() => row.OnBlur("  "));

        Assert.DoesNotContain(row, Rows(h));
    }

    [Theory]
    [InlineData("todos?tm-showing=9")]
    [InlineData("todos?tm-showing=abc")]
    public async Task TheAddress_AFilterItDoesNotKnowIsIgnored(string address)
    {
        var h = await Harness.Start(address);

        Assert.Equal(ShowingOptions.All, Todo(h).Footer!.Showing);
        Assert.Equal("todos", h.History.Token); // the garbage leaves the address on its own
    }

    [Fact]
    public async Task TheAddress_AListIdItCannotReadIsIgnored()
    {
        var h = await Harness.Start("todos?tm-uid=abc");

        Assert.Equal(5, Rows(h).Count);
        Assert.False(Todo(h).StressMode);
    }

    [Fact]
    public async Task StressMode_GoesInAndBackOutThroughTheAddress_WithoutFlippingToItself()
    {
        var h = await Harness.Start("todos");
        var presenter = h.PresenterOf<TodoMvcPresenter>(Places.Todos);

        await h.Do(Todo(h).OnToggleStress);
        Assert.True(Todo(h).StressMode);
        Assert.Equal(1000, Rows(h).Count);
        Assert.NotNull(Todo(h).List!.Clock);
        Assert.Equal("todos?tm-uid=-1", h.History.Token);

        await h.Do(Todo(h).OnToggleStress);
        Assert.False(Todo(h).StressMode);
        Assert.Null(Todo(h).List!.Clock);
        Assert.Equal("todos", h.History.Token);
        Assert.Same(presenter, h.PresenterOf<TodoMvcPresenter>(Places.Todos)); // the same place, not a new flip
    }

    [Fact]
    public async Task StressMode_TheClockTicks_WithoutMarkingEveryItem()
    {
        var h = await Harness.Start("todos?tm-uid=-1");
        var clock = Todo(h).List!.Clock!;
        var recorder = new ScopeRenderRecorder();
        await h.On(() => recorder.Attach([clock, .. Rows(h).Take(50)]));
        var before = clock.Date;

        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.App.Settle();

        Assert.True(clock.Date > before);
        Assert.Equal(1, recorder.CountFor(clock));
        Assert.All(Rows(h).Take(50), r => Assert.Equal(0, recorder.CountFor(r)));
    }

    [Fact]
    public async Task Reload_ReusesTheRowOfTheSameToDo_InsteadOfRebuildingTheList()
    {
        // The TS tutorial rebuilt every row on each load (see the bug report); here rows are reconciled by identity.
        var h = await Harness.Start("todos");
        var first = Rows(h)[0];

        await h.Do(Todo(h).OnToggleStress); // a different list that also has an item with id 1
        Assert.Same(first, Rows(h)[0]);
        Assert.Equal("Item 0", first.Title); // every field rewritten

        await h.Do(Todo(h).OnToggleStress);
        Assert.Same(first, Rows(h)[0]);
        Assert.Equal("Walk the dog", first.Title);
    }
}
