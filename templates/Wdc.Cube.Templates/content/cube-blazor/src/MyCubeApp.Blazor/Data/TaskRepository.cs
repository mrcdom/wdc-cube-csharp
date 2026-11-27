using MyCubeApp.Domain;
using Wdc.Domain;
using Wdc.Persistence;

namespace MyCubeApp.Blazor.Data;

/// <summary>The tasks in their table: how each field is stored, and how each criterion and ordering turn into SQL.</summary>
public sealed class TaskRepository(Database database)
    : SqlRepository<TaskItem, TaskCriteria, long>(database, Mapping), ITaskRepository
{
    public static readonly EntityMapping<TaskItem> Mapping = EntityMapping.For(TaskItem.Model, "task", "tk")
        .Key("id", generated: true)
        .Column("title")
        .Column("done")
        .Conditions<TaskCriteria>(Conditions)
        .Ordering<TaskOrderBy>(o => o switch
        {
            TaskOrderBy.TitleAToZ => [SortSpec.Asc("title"), SortSpec.Asc("id")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    private static ISqlFragment? Conditions(TaskCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.TaskId),
        t.Translate("title", criteria.Title),
        t.Translate("done", criteria.Done),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);
}
