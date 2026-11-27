using Avalonia.Markup.Xaml;
using Shopping.I18n;

namespace Shopping.Avalonia.Views;

/// <summary>
/// A fixed text of a view, in the language of the current UI culture: <c>Text="{v:T cart.title}"</c>. Written when the
/// view loads; the language does not change while the app runs.
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    /// <summary>The key in the catalog.</summary>
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Messages.Current.Text(Key);
}
