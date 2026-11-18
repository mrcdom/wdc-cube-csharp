using System.Text;
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

/// <summary>Reads and writes criteria in the wire format shared with the Kotlin version.</summary>
/// <remarks>
/// <code>{ "price": { "p": [ { "o": "GE", "v": [10.5] }, { "o": "LE", "v": [50.5] } ] },
///   "name": { "or": true, "p": [ { "o": "LIKE", "v": ["Bola%"] }, { "o": "EQ", "v": ["Fita"] } ] },
///   "productId": 3,
///   "orderBy": "CHEAPEST_FIRST" }</code>
/// A field not set writes nothing. A loose value reads as equality. An unknown operator is dropped (the filter gets
/// looser, never different); an unknown ordering is refused (<see cref="InvalidRequestException"/>).
/// </remarks>
public static class CriteriaCodec
{
    public const string OrderByProperty = "orderBy";

    /// <summary>Writes the criteria as an object.</summary>
    public static void Write(Utf8JsonWriter writer, ICriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        WriteFields(writer, criteria);
        writer.WriteEndObject();
    }

    /// <summary>Writes the criteria's properties into an object already open (a request body with more keys).</summary>
    public static void WriteFields(Utf8JsonWriter writer, ICriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(criteria);
        foreach (var criterion in criteria.Criterions)
        {
            if (criterion.IsSet)
            {
                criterion.WriteJson(writer);
            }
        }

        if (criteria.OrderByName is { } orderBy)
        {
            writer.WriteString(OrderByProperty, orderBy);
        }
    }

    /// <summary>Reads an object into <paramref name="criteria"/>, skipping keys it does not know.</summary>
    public static void Read(ref Utf8JsonReader reader, ICriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidRequestException("criteria must be a JSON object");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            reader.Read();
            if (!TryReadField(ref reader, criteria, name))
            {
                reader.Skip();
            }
        }
    }

    /// <summary>
    /// Reads the value at the reader into the criteria field <paramref name="name"/>. Returns false when the name is
    /// not a field of the criteria (the caller skips the value or reads it as something else).
    /// </summary>
    public static bool TryReadField(ref Utf8JsonReader reader, ICriteria criteria, string name)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (name == OrderByProperty)
        {
            criteria.SetOrderByName(reader.TokenType == JsonTokenType.Null ? null : reader.GetString());
            return true;
        }

        if (criteria.Find(name) is not { } criterion)
        {
            return false;
        }

        criterion.ReadJson(ref reader);
        return true;
    }

    public static string ToJson(ICriteria criteria) => EntityJson.Serialize(w => Write(w, criteria));

    public static TCriteria FromJson<TCriteria>(string json, TCriteria criteria)
        where TCriteria : ICriteria
    {
        var reader = EntityJson.Start(json);
        Read(ref reader, criteria);
        return criteria;
    }
}

internal static class EntityJson
{
    public static void WriteObject<TEntity>(Utf8JsonWriter writer, EntityModel<TEntity> model, TEntity entity,
        TEntity? projection, HashSet<object> visited)
        where TEntity : class
    {
        if (!visited.Add(entity))
        {
            // Met before in this graph: the key alone, which breaks cycles and repeats.
            writer.WriteStartObject();
            if (model.Key is { } key && key.HasValue(entity))
            {
                key.WriteJson(writer, entity, null, visited);
            }

            writer.WriteEndObject();
            return;
        }

        writer.WriteStartObject();
        foreach (var field in model.Fields)
        {
            var wanted = projection is null ? field.HasValue(entity) : field.HasValue(projection);
            if (wanted)
            {
                field.WriteJson(writer, entity, projection, visited);
            }
        }

        writer.WriteEndObject();
    }

    public static TEntity ReadObject<TEntity>(ref Utf8JsonReader reader, EntityModel<TEntity> model, TEntity? presence)
        where TEntity : class
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidRequestException($"{model.Name} must be a JSON object");
        }

        var entity = model.New();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            reader.Read();
            if (model.Find(name) is { } field)
            {
                field.ReadJson(ref reader, entity, presence);
            }
            else
            {
                reader.Skip(); // unknown key: ignored
            }
        }

        return entity;
    }

    public static string Serialize(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static Utf8JsonReader Start(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        if (!reader.Read())
        {
            throw new InvalidRequestException("empty JSON");
        }

        return reader;
    }
}
