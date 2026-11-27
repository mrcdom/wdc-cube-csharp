using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.AspNetCore.TestHost;
using Shopping.Avalonia.Views;
using Shopping.Client;
using Shopping.Presentation.Modules;
using Shopping.Presentation.Tests;
using Wdc.Cube;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Tests;

/// <summary>One headless Avalonia per process (it initializes once).</summary>
internal static class Headless
{
    private static readonly Lazy<HeadlessUnitTestSession> s_session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)));

    /// <summary>
    /// Runs <paramref name="test"/> on the UI thread and waits for all of it. Through <c>Func&lt;Task&lt;T&gt;&gt;</c> on
    /// purpose: a <c>Func&lt;Task&gt;</c> binds to <c>Dispatch&lt;TResult&gt;(Func&lt;TResult&gt;)</c>, which returns
    /// at the first <c>await</c> and lets a test pass without running.
    /// </summary>
    public static Task Run(Func<Task> test) => s_session.Value.Dispatch(async () =>
    {
        await test();
        return true;
    }, TestContext.Current.CancellationToken);
}

/// <summary>
/// The desktop Shopping, end to end: Avalonia views over the same presentation, repositories and login through the
/// Shopping server.
/// </summary>
public sealed class DesktopTests(ShoppingHttpFixture db) : IClassFixture<ShoppingHttpFixture>
{
    private static readonly string s_frames = Path.Combine(AppContext.BaseDirectory, "frames");
    private readonly ErrorLog _log = new();

    /// <summary>Keeps what the presenters log as errors, to tell why a wait timed out.</summary>
    private sealed class ErrorLog : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel >= Microsoft.Extensions.Logging.LogLevel.Error;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Errors.Add($"{formatter(state, exception)} {exception}");
            }
        }
    }

    private sealed class Desktop(MainWindow window, RootPresenter presenter, MemoryHistoryManager history, ErrorLog log)
    {
        public MainWindow Window { get; } = window;

        public RootPresenter Presenter { get; } = presenter;

        public MemoryHistoryManager History { get; } = history;

        public T Find<T>(string name)
            where T : Control =>
            Window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} named '{name}' on screen.");

        public IEnumerable<T> All<T>()
            where T : Control => Window.GetVisualDescendants().OfType<T>();

        public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public Button ButtonWith(string text) =>
            All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && (b.Content as string) == text)
            ?? throw new InvalidOperationException($"No visible button '{text}'.");

        public async Task Until(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (true)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // the render pass, as a real screen would run it
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
                if (condition())
                {
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"Waited 15 s for: {what}. Error toast: {Presenter.Scope.ErrorMessage?.ToString() ?? "(none)"}. "
                                               + $"Logged: {string.Join(" | ", log.Errors)}");
                }

                await Task.Delay(20);
            }
        }

        public void Capture(string name)
        {
            Directory.CreateDirectory(s_frames);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Window.CaptureRenderedFrame()?.Save(Path.Combine(s_frames, name + ".png"), new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    private async Task<Desktop> Open()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var http = db.Server.GetTestClient();
        typeof(App).GetProperty(nameof(App.Http))!.SetValue(null, http); // the images come from the same server
        var (presenter, history) = App.Compose(new ShoppingClient(http), logger: _log);
        var window = new MainWindow(presenter, history) { Width = 1100, Height = 760 };
        window.Show();
        var desktop = new Desktop(window, presenter, history, _log);
        await presenter.Start();
        await desktop.Until(() => desktop.All<LoginView>().Any(), "the login screen");
        return desktop;
    }

    private static async Task Login(Desktop desktop, string user)
    {
        desktop.Find<TextBox>("UserBox").Text = user;
        desktop.Find<TextBox>("PasswordBox").Text = user;
        Desktop.Click(desktop.Find<Button>("EnterButton"));
        await desktop.Until(() => desktop.All<HomeView>().Any() && desktop.All<ProductsPanelView>().Any(), "home");
    }

    [Fact]
    public Task InPortuguese_TheFixedTextsAndTheMessagesOfThePresentation() => Headless.Run(async () =>
    {
        // As the system sets it: for every thread, so the work the dispatcher runs outside this test's flow sees it too.
        var english = CultureInfo.DefaultThreadCurrentUICulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        try
        {
            var desktop = await Open();
            Assert.Contains(desktop.All<TextBlock>(), t => t.Text == "Entre para continuar" && t.IsEffectivelyVisible);
            Assert.Contains(desktop.All<TextBlock>(), t => t.Text == "Senha" && t.IsEffectivelyVisible);

            await Login(desktop, "fulano");
            await desktop.Until(() => desktop.Find<TextBlock>("Greeting").Text == "Olá, John Doe", "the greeting");
            Assert.Contains(desktop.All<TextBlock>(), t => t.Text == "Produtos" && t.IsEffectivelyVisible);
            Assert.Contains(desktop.All<TextBlock>(), t => t.Text == "Sair" && t.IsEffectivelyVisible);
            await desktop.Until(() => desktop.All<TextBlock>().Any(t => t.Text is { } text && text.EndsWith(" itens", StringComparison.Ordinal)), "the count");
            desktop.Capture("pt-BR-home");

            Desktop.Click(desktop.Find<Button>("CartButton"));
            await desktop.Until(() => desktop.All<CartView>().Any(), "the cart");
            Assert.Contains(desktop.All<TextBlock>(), t => t.Text == "Carrinho de compras" && t.IsEffectivelyVisible);
            Assert.Contains(desktop.All<TextBlock>(), t => t.Text == "Seu carrinho está vazio" && t.IsEffectivelyVisible);
            Assert.NotNull(desktop.ButtonWith("Continuar comprando"));

            desktop.Presenter.Release();
            desktop.Window.Close();
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = english;
        }
    });

    [Fact]
    public Task Login_Home_Product_Cart_Buy_Receipt_AndBack() => Headless.Run(async () =>
    {
        var desktop = await Open();
        desktop.Capture("1-login");

        await Login(desktop, "fulano");
        await desktop.Until(() => desktop.Find<TextBlock>("Greeting").Text == "Hello, John Doe", "the greeting");
        await desktop.Until(() => desktop.All<Button>().Count(b => b.Classes.Contains("product-card")) == 4, "four products");
        await desktop.Until(() => desktop.All<Image>().Count(i => i.Source is not null) >= 4, "the product images");
        desktop.Capture("2-home");

        Desktop.Click(desktop.All<Button>().First(b => b.Classes.Contains("product-card")));
        await desktop.Until(() => desktop.All<ProductView>().Any(), "the product");
        Desktop.Click(desktop.ButtonWith("+"));
        await desktop.Until(() => desktop.All<Image>().Any(i => i.Source is not null && i.Bounds.Width >= 100), "the product image");
        desktop.Capture("3-product");
        Desktop.Click(desktop.Find<Button>("AddButton"));
        await desktop.Until(() => desktop.Find<TextBlock>("CartBadge").Text == "2", "the cart badge");

        Desktop.Click(desktop.Find<Button>("CartButton"));
        await desktop.Until(() => desktop.All<CartLineView>().Count() == 1, "one cart line");
        Assert.Equal("2", desktop.Find<TextBlock>("Quantity").Text);
        desktop.Capture("4-cart");

        Desktop.Click(desktop.Find<Button>("BuyButton"));
        await desktop.Until(() => desktop.All<ReceiptView>().Any() && desktop.Find<TextBlock>("Total").Text is { Length: > 0 }, "the receipt");
        Assert.Contains(desktop.All<TextBlock>(), t => t.Text == "Purchase completed successfully!" && t.IsEffectivelyVisible);
        desktop.Capture("5-receipt");

        Assert.True(desktop.History.CanGoBack);
        desktop.History.GoBack();
        await desktop.Until(() => !desktop.All<ReceiptView>().Any(), "leaving the receipt");

        // Every scope on screen ended clean: the views confirmed each render.
        await desktop.Until(() => desktop.All<ScopeView>().All(v => v.Scope!.RenderState == ScopeRenderState.Clean), "clean scopes");
        desktop.Presenter.Release();
        desktop.Window.Close();
    });

    [Fact]
    public Task Admin_FiltersByCustomer_AndCorrectsAPurchase() => Headless.Run(async () =>
    {
        var desktop = await Open();
        await Login(desktop, "admin");
        Desktop.Click(desktop.Find<Button>("AdminButton"));
        await desktop.Until(() => desktop.All<AdminView>().Any() && desktop.All<Button>().Any(b => b.Classes.Contains("purchase-card")
                                                                                                && b.IsEffectivelyVisible), "the administration");
        desktop.Find<TextBox>("CustomerBox").Text = "Smith";
        Desktop.Click(desktop.ButtonWith("Filter"));
        await desktop.Until(() => AdminRows(desktop) is { Count: > 0 } rows && rows.All(r => r.CustomerLabel == "John Smith"),
            "only the admin's purchases");
        desktop.Capture("6-admin");

        // The demo purchase of 2011: a ball and a tape. The tape was not delivered.
        Desktop.Click(desktop.All<Button>().First(b => b is { IsEffectivelyVisible: true, DataContext: Presentation.Modules.Home.Admin.AdminPurchaseRow row } && Shopping.I18n.Messages.Default.Text(row.DateLabel) == "04/03/2011"));
        await desktop.Until(() => desktop.All<AmendLineView>().Count() == 2, "the purchase's items");
        Assert.False(desktop.Find<Button>("ConfirmButton").IsEnabled);
        var tape = desktop.All<AmendLineView>().Last();
        Desktop.Click(tape.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "−"));
        desktop.Find<TextBox>("ReasonBox").Text = "Tape not delivered";
        await desktop.Until(() => desktop.Find<Button>("ConfirmButton").IsEnabled, "the confirmation offered");
        desktop.Capture("7-amend");

        Desktop.Click(desktop.Find<Button>("ConfirmButton"));
        await desktop.Until(() => desktop.Find<TextBlock>("Success").Text == "Correction recorded.", "the correction registered");
        Assert.Single(desktop.All<AmendLineView>());
        Assert.Equal(string.Empty, desktop.Find<TextBox>("ReasonBox").Text);
        Assert.Contains(desktop.All<TextBlock>(), t => t.Text == "Tape not delivered" && t.IsEffectivelyVisible);
        desktop.Capture("8-amended");

        Desktop.Click(desktop.ButtonWith("Back"));
        await desktop.Until(() => desktop.All<TextBlock>().Any(t => t.Text == "corrected" && t.IsEffectivelyVisible), "the list, marked");
        Assert.Equal("Smith", desktop.Find<TextBox>("CustomerBox").Text);
        desktop.Presenter.Release();
        desktop.Window.Close();
    });

    private static List<Presentation.Modules.Home.Admin.AdminPurchaseRow> AdminRows(Desktop desktop) =>
        [.. desktop.All<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.DataContext)
            .OfType<Presentation.Modules.Home.Admin.AdminPurchaseRow>()];

    [Fact]
    public Task WrongCredentials_StayOnLogin_WithTheMessage() => Headless.Run(async () =>
    {
        var desktop = await Open();
        desktop.Find<TextBox>("UserBox").Text = "fulano";
        desktop.Find<TextBox>("PasswordBox").Text = "wrong";
        Desktop.Click(desktop.Find<Button>("EnterButton"));

        await desktop.Until(() => desktop.All<TextBlock>().Any(t => t.Text == Shopping.I18n.Messages.Default.Text(Presentation.Modules.Login.LoginPresenter.NotRecognized)
                                                                    && t.IsEffectivelyVisible), "the message");
        Assert.Null(desktop.Presenter.Subject);
        desktop.Presenter.Release();
        desktop.Window.Close();
    });
}
