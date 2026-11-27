using MyCubeApp.Detail;
using MyCubeApp.List;
using Wdc.Cube;

namespace MyCubeApp.Blazor.Views;

/// <summary>
/// The text of each message the presenters put in the scopes: they say what happened (a key and its arguments), the
/// view says it in words. One language here; a catalog per language goes in the same place.
/// </summary>
public static class Texts
{
    private static readonly Dictionary<string, string> s_texts = new(StringComparer.Ordinal)
    {
        [TasksApp.UnexpectedError.Key] = "Something went wrong. Try again.",
        [TaskListPresenter.TitleRequired.Key] = "Type what there is to do.",
        [TaskListPresenter.TaskNotFound.Key] = "That task no longer exists.",
        [TaskPresenter.TitleRequired.Key] = "A task needs a title.",
    };

    /// <summary>The text of <paramref name="message"/>: empty for none, the key for one without a text.</summary>
    public static string T(Message? message) =>
        message is null ? string.Empty : s_texts.GetValueOrDefault(message.Key, message.Key);
}
