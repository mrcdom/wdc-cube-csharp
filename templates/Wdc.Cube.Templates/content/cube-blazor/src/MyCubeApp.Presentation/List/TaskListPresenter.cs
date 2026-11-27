using MyCubeApp.Detail;
using MyCubeApp.Domain;
using Wdc.Cube;

namespace MyCubeApp.List;

/// <summary>
/// The list of tasks. A presenter only fills scopes and binds their actions: it never creates or holds a view, so it
/// runs the same with any rendering technology, and with none in the tests.
/// </summary>
public sealed class TaskListPresenter : CubePresenter<TasksApp, TaskListScope>
{
    public static Message TitleRequired { get; } = new("tasks.titleRequired");

    public static Message TaskNotFound { get; } = new("tasks.taskNotFound");

    /// <summary>The rows alive, by task id: a row whose task stays is reused, so only what changed in it repaints.</summary>
    private readonly Dictionary<long, TaskRowScope> _rows = [];

    public TaskListPresenter(TasksApp app)
        : base(app, new TaskListScope())
    {
        // Each action calls the method of the same name.
        Scope.OnAdd = Action<string>(OnAdd);
    }

    /// <summary>
    /// Every navigation through this place calls it: <paramref name="initialization"/> the first time,
    /// <paramref name="last"/> when the list is the screen asked for (no detail open).
    /// </summary>
    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new TaskListKeys(App, intent);
        keys.ParentSlot(Scope); // into the application's body

        if (initialization || keys.TaskChanged)
        {
            await LoadTasks();
        }

        if (last)
        {
            Scope.Detail = null;
        }
        else
        {
            keys.ParentSlot = detail => Scope.Detail = detail; // the task shows beside the list
        }

        return true;
    }

    /// <summary>Derived state, once per repaint, after every change of the round.</summary>
    protected override void OnBeforeScopeUpdate() => Scope.Remaining = Scope.Rows.Count(r => !r.Done);

    private async Task OnAdd(string title)
    {
        var text = title.Trim();
        if (text.Length == 0)
        {
            Scope.ErrorMessage = TitleRequired;
            return;
        }

        var task = new TaskItem { Title = text, Done = false };
        await App.Tasks.Insert(task);
        Scope.ErrorMessage = null;
        Scope.Rows.Add(Fill(NewRow(task.Id!.Value), task));
    }

    private async Task OnToggle(long taskId)
    {
        if (!_rows.TryGetValue(taskId, out var row))
        {
            return;
        }

        if (!await App.Tasks.Update(new TaskItem { Id = taskId, Done = !row.Done }))
        {
            // Removed elsewhere in the meantime: say so, and show what there is now.
            Scope.ErrorMessage = TaskNotFound;
            await LoadTasks();
            return;
        }

        Scope.ErrorMessage = null;
        row.Done = !row.Done;
    }

    private Task OnOpen(long taskId) => new TaskKeys(App) { TaskId = taskId }.Flip();

    /// <summary>
    /// Makes the rows follow the tasks: reuses those whose task stays, creates the new ones, forgets the ones whose
    /// task left.
    /// </summary>
    private async Task LoadTasks()
    {
        var tasks = await App.Tasks.Fetch(new TaskCriteria().WithOrderBy(TaskOrderBy.OldestFirst));
        var ids = tasks.Select(t => t.Id!.Value).ToHashSet();
        foreach (var gone in _rows.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _rows.Remove(gone);
        }

        Scope.Rows.Assign([.. tasks.Select(t => Fill(_rows.GetValueOrDefault(t.Id!.Value) ?? NewRow(t.Id.Value), t))]);
    }

    private TaskRowScope NewRow(long taskId)
    {
        var row = new TaskRowScope { Identity = taskId, Id = taskId };
        row.OnToggle = Action(() => OnToggle(taskId));
        row.OnOpen = Action(() => OnOpen(taskId));
        _rows[taskId] = row;
        return row;
    }

    private static TaskRowScope Fill(TaskRowScope row, TaskItem task)
    {
        row.Title = task.Title ?? string.Empty;
        row.Done = task.Done ?? false;
        return row;
    }
}
