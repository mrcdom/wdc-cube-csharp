using System.Net;
using System.Text.Json;
using Wdc.Domain;

namespace Wdc.Rest;

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
            TransactionNotFoundException => (HttpStatusCode.Gone, error.Message),
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
            HttpStatusCode.Gone => new TransactionNotFoundException(text),
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
