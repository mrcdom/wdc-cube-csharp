using System.Text.Json;

namespace Wdc.Domain;

/// <summary>Reads and writes criteria in the wire format shared with the Kotlin version.</summary>
/// <remarks>
/// <code>{ "price": { "p": [ { "o": "GE", "v": [10.5] }, { "o": "LE", "v": [50.5] } ] },
///   "name": { "or": true, "p": [ { "o": "LIKE", "v": ["Volley%"] }, { "o": "EQ", "v": ["Thread Seal Tape"] } ] },
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
