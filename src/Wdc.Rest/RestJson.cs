using System.Buffers;
using System.Text.Json;
using Wdc.Domain;

namespace Wdc.Rest;

/// <summary>Small helpers over <see cref="Utf8JsonWriter"/> and <see cref="Utf8JsonReader"/>.</summary>
public static class RestJson
{
    public static byte[] Write(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A reader at the first token of <paramref name="body"/>.</summary>
    public static Utf8JsonReader Start(ReadOnlySpan<byte> body)
    {
        var reader = new Utf8JsonReader(body);
        if (!reader.Read())
        {
            throw new InvalidRequestException("empty JSON body");
        }

        return reader;
    }

    /// <summary>Visits each property of the object at the reader; <paramref name="visit"/> must consume the value.</summary>
    public static void ReadObject(ref Utf8JsonReader reader, PropertyVisitor visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidRequestException("a JSON object was expected");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            reader.Read();
            visit(name, ref reader);
        }
    }

    public static int ReadInt(ref Utf8JsonReader reader, string name) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value)
            ? value
            : throw new InvalidRequestException($"'{name}' must be an integer");

    /// <summary>Receives one property of an object; the reader is at its value.</summary>
    public delegate void PropertyVisitor(string name, ref Utf8JsonReader reader);
}
