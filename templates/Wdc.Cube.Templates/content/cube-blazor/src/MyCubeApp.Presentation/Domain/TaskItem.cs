using Wdc.Domain;

namespace MyCubeApp.Domain;

/// <summary>A task to do.</summary>
public sealed class TaskItem : IKeyedEntity
{
    /// <summary>Its fields, as the repositories read and write them: no reflection involved.</summary>
    public static readonly EntityModel<TaskItem> Model = EntityModel.For(() => new TaskItem())
        .Key(ValueKinds.Int64, "id", t => t.Id, (t, v) => t.Id = v)
        .Field(ValueKinds.String, "title", t => t.Title, (t, v) => t.Title = v)
        .Field(ValueKinds.Boolean, "done", t => t.Done, (t, v) => t.Done = v)
        .Build();

    public long? Id { get; set; }

    public string? Title { get; set; }

    public bool? Done { get; set; }

    object? IKeyedEntity.Key => Id;
}
