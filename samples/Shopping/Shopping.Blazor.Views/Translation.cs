using Shopping.I18n;
using Wdc.Cube;

namespace Shopping.Blazor.Views;

/// <summary>
/// How the views write text, in the language of the current UI culture: <c>@T(Scope.ErrorMessage)</c> for a message
/// of the presentation, <c>@T(MessageKeys.CartTitle)</c> for a fixed text.
/// </summary>
public static class Translation
{
    /// <summary>The text of <paramref name="message"/>; empty for none.</summary>
    public static string T(Message? message) => Messages.Current.Text(message);

    /// <summary>The text of <paramref name="key"/>.</summary>
    public static string T(string key) => Messages.Current.Text(key);
}
