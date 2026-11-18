using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Shopping.Avalonia.Views;

/// <summary>
/// <c>RemoteImage.Url</c> on an <see cref="Image"/>: loads the picture from the Shopping server (the same relative
/// address the web app uses, <c>/image/product/3.png</c>), once per address.
/// </summary>
public sealed class RemoteImage : AvaloniaObject
{
    public static readonly AttachedProperty<string?> UrlProperty =
        AvaloniaProperty.RegisterAttached<RemoteImage, Image, string?>("Url");

    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> s_cache = new(StringComparer.Ordinal);

    static RemoteImage() => UrlProperty.Changed.AddClassHandler<Image>((image, e) => _ = Load(image, e.NewValue as string));

    public static string? GetUrl(Image image) => image.GetValue(UrlProperty);

    public static void SetUrl(Image image, string? value) => image.SetValue(UrlProperty, value);

    private static async Task Load(Image image, string? url)
    {
        image.Source = null;
        if (string.IsNullOrEmpty(url) || App.Http is null)
        {
            return;
        }

        var bitmap = await s_cache.GetOrAdd(url, Fetch);
        if (GetUrl(image) == url)
        {
            await Dispatcher.UIThread.InvokeAsync(() => image.Source = bitmap);
        }
    }

    private static async Task<Bitmap?> Fetch(string url)
    {
        try
        {
            using var response = await App.Http.GetAsync(url);
            if (!response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.Position = 0;
            return new Bitmap(copy);
        }
        catch (HttpRequestException)
        {
            return null; // no picture is better than no screen
        }
    }
}
