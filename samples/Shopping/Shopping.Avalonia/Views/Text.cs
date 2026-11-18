using System.Globalization;
using Avalonia.Data.Converters;

namespace Shopping.Avalonia.Views;

/// <summary>Formats an int for display in the current culture (counts in the markup).</summary>
public static class Text
{
    public static readonly IValueConverter Number = new FuncValueConverter<int, string>(v => v.ToString(CultureInfo.CurrentCulture));
}
