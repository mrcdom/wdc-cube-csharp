using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shopping.Presentation.Infra;

internal static class SubjectSession
{
    public const string Key = "shopping.subject";

    public static string Write(Subject subject) => JsonSerializer.Serialize(subject, SubjectJson.Default.Subject);

    public static Subject? Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, SubjectJson.Default.Subject);
        }
        catch (JsonException)
        {
            return null; // unreadable: as if nobody were logged in
        }
    }
}

// Generated at build time: no reflection, so it holds under trimming and AOT.
[JsonSerializable(typeof(Subject))]
internal sealed partial class SubjectJson : JsonSerializerContext;
