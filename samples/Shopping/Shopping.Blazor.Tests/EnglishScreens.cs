using System.Globalization;
using System.Runtime.CompilerServices;

namespace Shopping.Blazor.Tests;

/// <summary>
/// The screens in English, whatever the language of the machine: the views write in the UI culture, and the tests
/// read the texts in English unless they set another language.
/// </summary>
internal static class EnglishScreens
{
    [ModuleInitializer]
    internal static void Set()
    {
        // The default of every thread, not this flow's own: one set here would flow to every thread started from it and
        // win over the language a test sets for all of them.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }
}
