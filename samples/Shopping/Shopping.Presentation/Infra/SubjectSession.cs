using System.Text.Json;

namespace Shopping.Presentation.Infra;

internal static class SubjectSession
{
    public const string Key = "shopping.subject";

    public static string Write(Subject subject) => JsonSerializer.Serialize(subject);

    public static Subject? Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Subject>(json);
        }
        catch (JsonException)
        {
            return null; // unreadable: as if nobody were logged in
        }
    }
}
