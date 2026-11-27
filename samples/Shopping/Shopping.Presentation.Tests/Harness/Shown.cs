using Shopping.I18n;
using Wdc.Cube;

namespace Shopping.Presentation.Tests;

/// <summary>What a view shows of a message, in the default language (the tests read the screens in English).</summary>
internal static class Shown
{
    public static string En(Message? message) => Messages.Default.Text(message);

    public static IEnumerable<string> En(IEnumerable<Message> messages) => messages.Select(m => En(m));
}
