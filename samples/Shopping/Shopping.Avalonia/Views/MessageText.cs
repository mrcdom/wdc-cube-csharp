using System.Globalization;
using Avalonia.Data.Converters;
using Shopping.I18n;
using Wdc.Cube;

namespace Shopping.Avalonia.Views;

/// <summary>
/// How a view writes a message of the presentation, in the language of the current UI culture:
/// <c>Text="{Binding ErrorMessage, Converter={StaticResource T}}"</c> (the converter is a resource of the application,
/// keyed <c>T</c>).
/// </summary>
public sealed class MessageText : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Messages.Current.Text(value as Message);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A message is only written.");
}
