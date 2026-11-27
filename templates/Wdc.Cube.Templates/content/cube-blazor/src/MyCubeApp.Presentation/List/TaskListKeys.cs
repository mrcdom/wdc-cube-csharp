using Wdc.Cube;

namespace MyCubeApp.List;

/// <summary>The list of tasks, below the application: <c>tasks</c>. The keys of the detail extend these.</summary>
public class TaskListKeys : RootKeys
{
    public new const string Prefix = "tl";

    public static new Place Place { get; } = RootKeys.Place.Child<TasksApp>("tasks", app => new TaskListPresenter(app));

    private const string TaskChangedName = $"{Prefix}-task-changed";

    public TaskListKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected TaskListKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }

    /// <summary>
    /// Set by whoever changed a task elsewhere (the detail): the list loads the tasks again. An attribute, not a
    /// parameter: it travels with this navigation only, never in the address.
    /// </summary>
    public bool TaskChanged
    {
        get => Intent.GetAttribute<bool>(TaskChangedName);
        set => Intent.SetAttribute(TaskChangedName, value ? true : null);
    }
}
