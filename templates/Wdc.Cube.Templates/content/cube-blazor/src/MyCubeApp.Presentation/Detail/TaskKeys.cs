using MyCubeApp.List;
using Wdc.Cube;

namespace MyCubeApp.Detail;

/// <summary>The detail of one task, below the list: <c>tasks/task?tk-id=3</c>.</summary>
public sealed class TaskKeys : TaskListKeys
{
    public new const string Prefix = "tk";

    public static new Place Place { get; } = TaskListKeys.Place.Child<TasksApp>("task", app => new TaskPresenter(app));

    private const string TaskIdName = $"{Prefix}-id";

    public TaskKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }

    /// <summary>A parameter: it goes in the address, which is input from outside. Garbage reads as none.</summary>
    public long? TaskId
    {
        get => Intent.GetInt64(TaskIdName) is > 0 and var id ? id : null;
        set => Intent.SetParameter(TaskIdName, value);
    }
}
