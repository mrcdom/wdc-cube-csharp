using Microsoft.Extensions.Logging;
using Shopping.Domain;
using Shopping.Presentation.Infra;
using Wdc.Cube;
using Home = Shopping.Presentation.Modules.Home;

namespace Shopping.Presentation.Modules.Main;

/// <summary>
/// The application presenter: the page frame (body and error toast), the session (who is logged in) and the
/// repositories. Going to the application itself means going home.
/// </summary>
public sealed class MainPresenter : ApplicationPresenter<MainScope>
{
    /// <summary>How long the error toast stays.</summary>
    public static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(4);

    private ITimer? _errorTimer;

    public MainPresenter(HistoryManager history, ShoppingRepositories repositories, ISessionStore session,
        PresentationDispatcher? dispatcher = null, TimeProvider? timeProvider = null, ILogger? logger = null,
        IAuthentication? authentication = null)
        : base(new MainScope(), history, Places.All, logger, dispatcher, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(session);
        Repositories = repositories;
        Session = session;
        Authentication = authentication ?? new LocalAuthentication(repositories);
    }

    public ShoppingRepositories Repositories { get; }

    /// <summary>Opens and closes the session: in this process, or through the server.</summary>
    public IAuthentication Authentication { get; }

    public ISessionStore Session { get; }

    /// <summary>Who is logged in, or <see langword="null"/>.</summary>
    public Subject? Subject { get; private set; }

    /// <summary>Starts from the address; an empty address (or the application itself) goes home.</summary>
    public Task Start() => InvokeAsync(async () =>
    {
        // The session first: with it comes the key that opens a sealed address (F5 on one).
        Subject = await ResumeSession();
        InstallCodec(Subject);
        await KickStart(Places.Home);
        if (!HasNavigated)
        {
            await Flip(Places.Home);
        }
    });

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        if (initialization)
        {
            Scope.OnDismissError = Action(() => Scope.ErrorMessage = null);

        }

        if (last)
        {
            if (!initialization)
            {
                await FlipToIntent(intent.Redirect(Places.Home)); // the application itself means home
                return false;
            }

            Scope.Body = null;
            return true;
        }

        new MainKeys(this, intent).ParentSlot = body => Scope.Body = body;
        return true;
    }

    public async Task SignIn(Subject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        Subject = subject;
        InstallCodec(subject);
        await Session.Set(SubjectSession.Key, SubjectSession.Write(subject));
    }

    public async Task SignOut()
    {
        var leaving = Subject;
        Subject = null;
        InstallCodec(null);
        await Session.Remove(SubjectSession.Key);
        if (leaving is not null)
        {
            try
            {
                await Authentication.Logout(leaving);
            }
            catch (Exception caught) when (caught is HttpRequestException or TimeoutException)
            {
                Logger.LogWarning(caught, "Logging out on the server failed; the session is closed here.");
            }
        }
    }

    /// <summary>
    /// Seals the addresses with the user's key (decision D-22), or stops sealing. Where the codec cannot run
    /// (WebAssembly has no AES), the addresses stay plain.
    /// </summary>
    private void InstallCodec(Subject? subject)
    {
        if (subject?.HistoryKey is not { } key)
        {
            History.Codec = null;
            return;
        }

        try
        {
            History.Codec = new Wdc.Cube.Codec.AesHistoryCodec(Wdc.Cube.Codec.Base64Url.Decode(key));
        }
        catch (PlatformNotSupportedException)
        {
            History.Codec = null;
        }
    }

    private async Task<Subject?> ResumeSession()
    {
        if (SubjectSession.Read(await Session.Get(SubjectSession.Key)) is not { } stored)
        {
            return null;
        }

        Subject? resumed;
        try
        {
            resumed = await Authentication.Resume(stored);
        }
        catch (Exception caught) when (caught is HttpRequestException or TimeoutException)
        {
            Logger.LogWarning(caught, "Taking the session back failed; starting logged out.");
            resumed = null;
        }

        if (resumed is null)
        {
            await Session.Remove(SubjectSession.Key);
        }
        else if (resumed != stored)
        {
            await Session.Set(SubjectSession.Key, SubjectSession.Write(resumed)); // a renewed credential
        }

        return resumed;
    }

    /// <summary>Shows <paramref name="message"/> in the error toast for a few seconds.</summary>
    public void ShowError(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Post(() => ShowError(message));
            return;
        }

        Scope.ErrorMessage = message;
        _errorTimer?.Dispose();
        _errorTimer = Time.CreateTimer(_ => Dispatcher.Post(() =>
        {
            if (Scope.ErrorMessage == message)
            {
                Scope.ErrorMessage = null;
            }
        }), null, ErrorToastDuration, Timeout.InfiniteTimeSpan);
    }

    public const string GenericError = "Ocorreu um erro inesperado. Tente novamente em instantes.";

    /// <summary>
    /// Errors nobody handled: the detail goes to the log, the user reads a message meant for them. Refusals the
    /// application knows (a link to a product or receipt that is not there) say so; anything else is generic.
    /// </summary>
    public override void Unexpected(string message, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        base.Unexpected(message, error);
        ShowError(error switch
        {
            ProductNotFoundException => Home.HomePresenter.ProductNotFound,
            PurchaseNotFoundException => Home.HomePresenter.ReceiptNotFound,
            _ => GenericError,
        });
    }

    public override void Release()
    {
        _errorTimer?.Dispose();
        base.Release();
    }
}
