using MyCubeApp.Domain;
using MyCubeApp.List;
using Wdc.Cube;

namespace MyCubeApp.Detail;

/// <summary>The detail of one task, shown in the slot the list offers.</summary>
public sealed class TaskPresenter : CubePresenter<TasksApp, TaskScope>
{
    public static Message TitleRequired { get; } = new("task.titleRequired");

    private long? _taskId;

    public TaskPresenter(TasksApp app)
        : base(app, new TaskScope())
    {
        Scope.OnRename = Action<string>(OnRename);
        Scope.OnRemove = Action(OnRemove);
        Scope.OnClose = Action(OnClose);
    }

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new TaskKeys(App, intent);
        if (keys.TaskId is not { } taskId)
        {
            // Nothing to show: the list instead, within this same navigation.
            await App.FlipToIntent(intent.Redirect(TaskListKeys.Place));
            return false;
        }

        // A navigation reuses this presenter for another task: load only when the task asked for changed.
        if (initialization || taskId != _taskId)
        {
            if (await App.Tasks.FetchById(taskId) is not { } task)
            {
                await App.FlipToIntent(intent.Redirect(TaskListKeys.Place));
                return false;
            }

            _taskId = taskId;
            Scope.Id = taskId;
            Scope.Title = task.Title ?? string.Empty;
            Scope.ErrorMessage = null;
        }

        keys.ParentSlot(Scope); // beside the list
        return true;
    }

    /// <summary>What this place puts in the address, so that a reload or a link opens the same task.</summary>
    public override void PublishParameters(FlipIntent intent) => new TaskKeys(App, intent).TaskId = _taskId;

    private async Task OnRename(string title)
    {
        var text = title.Trim();
        if (text.Length == 0)
        {
            Scope.ErrorMessage = TitleRequired;
            return;
        }

        await App.Tasks.Update(new TaskItem { Id = _taskId, Title = text });
        await BackToTheList(taskChanged: true);
    }

    private async Task OnRemove()
    {
        await App.Tasks.Delete(new TaskCriteria().TaskId.Eq(_taskId));
        await BackToTheList(taskChanged: true);
    }

    private Task OnClose() => BackToTheList(taskChanged: false);

    /// <summary>Back to the list; telling it a task changed makes it load them again.</summary>
    private Task BackToTheList(bool taskChanged) => new TaskListKeys(App) { TaskChanged = taskChanged }.Flip();
}
