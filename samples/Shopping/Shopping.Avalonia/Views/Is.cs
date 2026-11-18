using System.Collections;
using Avalonia.Data.Converters;

namespace Shopping.Avalonia.Views;

/// <summary>Small converters for visibility in the markup.</summary>
public static class Is
{
    public static readonly IValueConverter Empty = new FuncValueConverter<object?, bool>(v => Count(v) == 0);

    public static readonly IValueConverter NotEmpty = new FuncValueConverter<object?, bool>(v => Count(v) > 0);

    public static readonly IValueConverter Positive = new FuncValueConverter<int, bool>(v => v > 0);

    private static int Count(object? value) => value switch
    {
        ICollection collection => collection.Count,
        IEnumerable enumerable => enumerable.Cast<object>().Count(),
        _ => 0,
    };
}
