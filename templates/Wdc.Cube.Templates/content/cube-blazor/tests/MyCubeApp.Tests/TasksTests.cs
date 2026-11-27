using MyCubeApp.Detail;
using MyCubeApp.Domain;
using MyCubeApp.List;
using Wdc.Cube.Testing;

namespace MyCubeApp.Tests;

/// <summary>The whole application, driven as a view would drive it, with no view at all.</summary>
public sealed class TasksTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(TasksApp App, TestHistoryManager History)> Start(TestDatabase db, string address = "")
    {
        var history = new TestHistoryManager(address);
        var app = new TasksApp(history, db.Tasks);
        await app.Start();
        await app.Settle();
        return (app, history);
    }

    private static TaskListScope List(TasksApp app) => Assert.IsType<TaskListScope>(app.Scope.Body);

    private static TaskScope Detail(TasksApp app) => Assert.IsType<TaskScope>(List(app).Detail);

    private static async Task Do(TasksApp app, Func<Task> action)
    {
        await action();
        await app.Settle();
    }

    [Fact]
    public async Task AnEmptyAddress_ShowsTheList()
    {
        await using var db = await TestDatabase.Create();
        var (app, history) = await Start(db);

        Assert.Equal(3, List(app).Rows.Count);
        Assert.Equal(2, List(app).Remaining);
        Assert.Null(List(app).Detail);
        Assert.Equal("tasks", history.Token);
    }

    [Fact]
    public async Task Adding_KeepsTheTask_AndCountsIt()
    {
        await using var db = await TestDatabase.Create();
        var (app, _) = await Start(db);

        await Do(app, () => List(app).OnAdd("  Write a test  "));

        Assert.Equal("Write a test", List(app).Rows[^1].Title);
        Assert.Equal(3, List(app).Remaining);
        Assert.Equal(4, await db.Tasks.Count(new TaskCriteria(), Ct));
    }

    [Fact]
    public async Task AddingNoTitle_SaysSo_AndAddsNothing()
    {
        await using var db = await TestDatabase.Create();
        var (app, _) = await Start(db);

        await Do(app, () => List(app).OnAdd("   "));

        Assert.Equal(TaskListPresenter.TitleRequired, List(app).ErrorMessage);
        Assert.Equal(3, List(app).Rows.Count);
    }

    [Fact]
    public async Task Toggling_KeepsIt_AndRecounts()
    {
        await using var db = await TestDatabase.Create();
        var (app, _) = await Start(db);
        var row = List(app).Rows.First(r => !r.Done);

        await Do(app, row.OnToggle);

        Assert.True(row.Done);
        Assert.Equal(1, List(app).Remaining);
        Assert.True((await db.Tasks.FetchById(row.Id, cancellationToken: Ct))!.Done);
    }

    [Fact]
    public async Task TogglingATaskRemovedElsewhere_SaysSo_AndReloads()
    {
        await using var db = await TestDatabase.Create();
        var (app, _) = await Start(db);
        var row = List(app).Rows[0];
        await db.Tasks.Delete(new TaskCriteria().TaskId.Eq(row.Id), Ct);

        await Do(app, row.OnToggle);

        Assert.Equal(TaskListPresenter.TaskNotFound, List(app).ErrorMessage);
        Assert.DoesNotContain(List(app).Rows, r => r.Id == row.Id);
    }

    [Fact]
    public async Task Opening_ShowsTheTaskBesideTheList_AndNamesItInTheAddress()
    {
        await using var db = await TestDatabase.Create();
        var (app, history) = await Start(db);
        var row = List(app).Rows[1];

        await Do(app, row.OnOpen);

        Assert.Equal(row.Title, Detail(app).Title);
        Assert.Equal($"tasks/task?tk-id={row.Id}", history.Token);
    }

    [Fact]
    public async Task TheAddressAlone_RebuildsTheScreen()
    {
        await using var db = await TestDatabase.Create();
        var id = (await db.Tasks.Fetch(new TaskCriteria().WithOrderBy(TaskOrderBy.OldestFirst), cancellationToken: Ct))[2].Id;

        var (app, _) = await Start(db, $"tasks/task?tk-id={id}");

        Assert.Equal(id, Detail(app).Id);
        Assert.Equal(3, List(app).Rows.Count);
    }

    [Theory]
    [InlineData("tasks/task?tk-id=999999")]
    [InlineData("tasks/task?tk-id=garbage")]
    [InlineData("tasks/task")]
    public async Task ATaskThatIsNotThere_FallsBackToTheList(string address)
    {
        await using var db = await TestDatabase.Create();

        var (app, history) = await Start(db, address);

        Assert.Null(List(app).Detail);
        Assert.Equal("tasks", history.Token);
    }

    [Fact]
    public async Task Renaming_GoesBackToTheList_WithTheNewTitle()
    {
        await using var db = await TestDatabase.Create();
        var (app, history) = await Start(db);
        var row = List(app).Rows[1];
        await Do(app, row.OnOpen);

        await Do(app, () => Detail(app).OnRename(" Renamed "));

        Assert.Null(List(app).Detail);
        Assert.Equal("tasks", history.Token);
        Assert.Same(row, List(app).Rows[1]); // the same row, repainted with the new title
        Assert.Equal("Renamed", row.Title);
    }

    [Fact]
    public async Task RenamingToNothing_SaysSo_AndStays()
    {
        await using var db = await TestDatabase.Create();
        var (app, _) = await Start(db);
        await Do(app, List(app).Rows[0].OnOpen);

        await Do(app, () => Detail(app).OnRename(""));

        Assert.Equal(TaskPresenter.TitleRequired, Detail(app).ErrorMessage);
    }

    [Fact]
    public async Task Removing_TakesItOutOfTheList()
    {
        await using var db = await TestDatabase.Create();
        var (app, _) = await Start(db);
        var id = List(app).Rows[0].Id;
        await Do(app, List(app).Rows[0].OnOpen);

        await Do(app, Detail(app).OnRemove);

        Assert.Null(List(app).Detail);
        Assert.DoesNotContain(List(app).Rows, r => r.Id == id);
        Assert.Null(await db.Tasks.FetchById(id, cancellationToken: Ct));
    }

    [Fact]
    public async Task Closing_GoesBackToTheList()
    {
        await using var db = await TestDatabase.Create();
        var (app, history) = await Start(db);
        await Do(app, List(app).Rows[0].OnOpen);

        await Do(app, Detail(app).OnClose);

        Assert.Null(List(app).Detail);
        Assert.Equal("tasks", history.Token);
    }
}
