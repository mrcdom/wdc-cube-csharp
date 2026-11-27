using Shopping.I18n;
using Wdc.Cube;

namespace Shopping.Blazor.Views;

/// <summary>How the views write a message of the presentation: <c>@T(Scope.ErrorMessage)</c>.</summary>
public static class Translation
{
    /// <summary>The text of <paramref name="message"/> in the views' language; empty for none.</summary>
    public static string T(Message? message) => Messages.Default.Text(message);
}
