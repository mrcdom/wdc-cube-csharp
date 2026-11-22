using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Shopping.Avalonia.Views;
using Shopping.Client;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Root;
using Wdc.Cube;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia;

public sealed partial class App : Application
{
    /// <summary>Where the Shopping server is.</summary>
    public static Uri ServerAddress { get; set; } = new("http://localhost:5200");

    /// <summary>The HTTP client to the server, shared by the repositories and the images.</summary>
    public static HttpClient Http { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        DataTemplates.Add(ViewCatalog.Create());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Http = new HttpClient { BaseAddress = ServerAddress, Timeout = TimeSpan.FromSeconds(30) };
            var (presenter, history) = Compose(new ShoppingClient(Http));
            desktop.MainWindow = new MainWindow(presenter, history);
            desktop.ShutdownRequested += (_, _) => presenter.Release();
            _ = presenter.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The application over the server: the same presenter as the web, other repositories and dispatcher.</summary>
    internal static (RootPresenter Presenter, MemoryHistoryManager History) Compose(ShoppingClient client, string address = "",
        Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        var history = new MemoryHistoryManager(address);
        var presenter = new RootPresenter(history, client.Repositories, new MemorySessionStore(),
            dispatcher: AvaloniaPresentation.CreateDispatcher(), logger: logger, authentication: client.Authentication);
        return (presenter, history);
    }
}
