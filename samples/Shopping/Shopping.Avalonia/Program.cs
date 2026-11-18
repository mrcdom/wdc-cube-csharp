using Avalonia;

namespace Shopping.Avalonia;

internal static class Program
{
    /// <summary>
    /// Starts the desktop app. The server is <c>--server http://host:port</c>, or <c>SHOPPING_SERVER</c>, or
    /// <c>http://localhost:5200</c> (the Shopping.Server profile).
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var index = Array.IndexOf(args, "--server");
        App.ServerAddress = new Uri(index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : Environment.GetEnvironmentVariable("SHOPPING_SERVER") ?? "http://localhost:5200");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
