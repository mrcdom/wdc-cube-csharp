using System.Text;
using System.Text.Json;

namespace Wdc.Domain;

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
