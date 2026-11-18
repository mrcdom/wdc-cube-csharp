using System.Buffers;
using System.Net;
using System.Text.Json;
using Wdc.Domain;

namespace Wdc.Rest;

/// <summary>Names on the wire, shared with the Kotlin version (decision D-18).</summary>
public static class RestNames
{
    /// <summary>The remote transaction a request belongs to.</summary>
    public const string TransactionHeader = "X-Tx-Id";

    /// <summary>Identifies one client instance (one app on one device); part of a remote transaction's owner.</summary>
    public const string ClientHeader = "X-Client-Id";

    public const string Insert = "insert";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Count = "count";
    public const string Fetch = "fetch";
    public const string FetchPage = "fetch-page";
    public const string FetchById = "fetch-by-id";

    public const string TransactionBase = "/api/tx";
}

/// <summary>Status ↔ exception, the same table on both sides; the body of an error is <c>{"error":"…"}</c>.</summary>
public static class RestErrors
{
    /// <summary>The status and the message the server answers for <paramref name="error"/>.</summary>
    /// <remarks>Anything not listed is a server fault: 500 with a generic message (the detail goes to the log only).</remarks>
    public static (HttpStatusCode Status, string Message) ToResponse(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error switch
        {
            InvalidRequestException or JsonException or FormatException => (HttpStatusCode.BadRequest, error.Message),
            AuthenticationException => (HttpStatusCode.Unauthorized, error.Message),
            AccessDeniedException => (HttpStatusCode.Forbidden, error.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, error.Message),
            TransactionConflictException => (HttpStatusCode.Conflict, error.Message),
            TransactionLimitExceededException => (HttpStatusCode.TooManyRequests, error.Message),
            _ => (HttpStatusCode.InternalServerError, "Internal server error"),
        };
    }

    /// <summary>The exception a client raises for an error answer.</summary>
    public static Exception FromResponse(HttpStatusCode status, string? message)
    {
        var text = string.IsNullOrEmpty(message) ? $"HTTP {(int)status}" : message;
        return status switch
        {
            HttpStatusCode.BadRequest => new InvalidRequestException(text),
            HttpStatusCode.Unauthorized => new AuthenticationException(text),
            HttpStatusCode.Forbidden => new AccessDeniedException(text),
            HttpStatusCode.NotFound => new KeyNotFoundException(text),
            HttpStatusCode.Conflict => new TransactionConflictException(text),
            HttpStatusCode.TooManyRequests => new TransactionLimitExceededException(text),
            _ => new RemoteCallException(status, text),
        };
    }

    /// <summary>Writes <c>{"error":"…"}</c>.</summary>
    public static byte[] Body(string message) => RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WriteString("error", message);
        w.WriteEndObject();
    });

    /// <summary>Reads the message of an error body; <see langword="null"/> when it is not one.</summary>
    public static string? MessageOf(ReadOnlySpan<byte> body)
    {
        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (name == "error" && reader.TokenType == JsonTokenType.String)
                {
                    return reader.GetString();
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}

/// <summary>A server answer that is not part of the contract (5xx, a proxy page).</summary>
public sealed class RemoteCallException : Exception
{
    public RemoteCallException()
    {
    }

    public RemoteCallException(string message)
        : base(message)
    {
    }

    public RemoteCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public RemoteCallException(HttpStatusCode status, string message)
        : base(message) => Status = status;

    public HttpStatusCode? Status { get; }
}

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
