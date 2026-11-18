using System.Text.Json;

namespace Wdc.Domain;

/// <summary>
/// Reads and writes an entity as JSON through its <see cref="EntityModel{TEntity}"/>: the same declaration serves the
/// SQL layer, the REST server and the HTTP client, so what one side writes the other reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Writing.</b> Without a projection, the fields that hold a value. With one, the fields it marks, with an
/// explicit <c>null</c> where the value is absent (that is how "clear this field" travels). An entity met twice in
/// the same graph is written as its key only (no cycles, no repeats).
/// </para>
/// <para>
/// <b>Reading for an update.</b> <see cref="ReadForUpdate(string)"/> also returns a projection marking every key that
/// was <i>present</i>, including the ones sent as <c>null</c>: "absent" and "set to null" stay different.
/// </para>
/// </remarks>
public sealed class JsonEntityCodec<TEntity>
    where TEntity : class
{
    public JsonEntityCodec(EntityModel<TEntity> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
    }

    public EntityModel<TEntity> Model { get; }

    public void Write(Utf8JsonWriter writer, TEntity entity, TEntity? projection = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entity);
        EntityJson.WriteObject(writer, Model, entity, projection, NewVisited());
    }

    public void WriteList(Utf8JsonWriter writer, IEnumerable<TEntity> entities, TEntity? projection = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entities);
        writer.WriteStartArray();
        foreach (var entity in entities)
        {
            EntityJson.WriteObject(writer, Model, entity, projection, NewVisited());
        }

        writer.WriteEndArray();
    }

    public string ToJson(TEntity entity, TEntity? projection = null) =>
        EntityJson.Serialize(w => Write(w, entity, projection));

    /// <summary>Reads an entity at the reader's current <c>{</c>.</summary>
    public TEntity Read(ref Utf8JsonReader reader) => EntityJson.ReadObject(ref reader, Model, null);

    public TEntity FromJson(string json)
    {
        var reader = EntityJson.Start(json);
        return Read(ref reader);
    }

    /// <summary>Reads an entity and the projection of the keys that were present (explicit nulls included).</summary>
    public (TEntity Entity, TEntity Presence) ReadForUpdate(ref Utf8JsonReader reader)
    {
        var presence = Model.New();
        var entity = EntityJson.ReadObject(ref reader, Model, presence);
        return (entity, presence);
    }

    public (TEntity Entity, TEntity Presence) ReadForUpdate(string json)
    {
        var reader = EntityJson.Start(json);
        return ReadForUpdate(ref reader);
    }

    private static HashSet<object> NewVisited() => new(ReferenceEqualityComparer.Instance);
}
