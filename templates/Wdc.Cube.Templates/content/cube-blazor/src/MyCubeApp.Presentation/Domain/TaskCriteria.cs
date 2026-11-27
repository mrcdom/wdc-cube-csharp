using Wdc.Domain;

namespace MyCubeApp.Domain;

/// <summary>Which tasks, in which order: <c>new TaskCriteria().Done.Eq(false)</c>.</summary>
public sealed class TaskCriteria : Criteria<TaskCriteria, TaskItem, TaskOrderBy>
{
    public TaskCriteria()
    {
        TaskId = Comparable(ValueKinds.Int64, "taskId");
        Title = Text("title");
        Done = Equality(ValueKinds.Boolean, "done");
    }

    public ComparableCriterion<TaskCriteria, long?> TaskId { get; }

    public TextCriterion<TaskCriteria> Title { get; }

    public Criterion<TaskCriteria, bool?> Done { get; }
}
