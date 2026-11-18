using Avalonia;
using Avalonia.Headless;

namespace Shopping.Avalonia.Tests;

/// <summary>Builds the headless app: the real App (styles, view catalog), Skia drawing for frame captures.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}
