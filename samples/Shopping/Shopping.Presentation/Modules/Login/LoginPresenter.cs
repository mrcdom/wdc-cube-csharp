using Shopping.I18n;
using Shopping.Presentation.Modules.Home;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Login;

/// <summary>Public place: asks for the credentials and, when they match, opens the session and goes home.</summary>
public sealed class LoginPresenter(RootPresenter app) : CubePresenter<RootPresenter, LoginScope>(app, new LoginScope())
{
    public static Message NotRecognized => Texts.LoginNotRecognized();

    public static Message CommunicationFailure => Texts.LoginCommunicationFailure();

    /// <summary>The message after too many failures: how long to wait, when known.</summary>
    public static Message TooManyAttempts(TimeSpan? wait) => wait is { } w
        ? Texts.LoginTooManyAttempts(seconds: Math.Max(1, (int)Math.Ceiling(w.TotalSeconds)))
        : Texts.LoginTooManyAttemptsLater();

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        if (initialization)
        {
            Scope.OnEnter = Action<string, string>(OnEnter);
        }

        new RootKeys(App, intent).ParentSlot(Scope);
        return Task.FromResult(true);
    }

    private async Task OnEnter(string userName, string password)
    {
        Scope.UserName = userName;
        Scope.ErrorMessage = null;
        Scope.Loading = true;
        try
        {
            var subject = await App.Authentication.Login(userName, password);
            if (subject is null)
            {
                Scope.ErrorMessage = NotRecognized;
                return;
            }

            await App.SignIn(subject);
            await new HomeKeys(App).Flip();
        }
        catch (Wdc.Domain.TooManyRequestsException refused)
        {
            Scope.ErrorMessage = TooManyAttempts(refused.RetryAfter);
        }
        finally
        {
            if (!IsReleased)
            {
                Scope.Loading = false;
            }
        }
    }

    /// <summary>The database or the server not answering is said on the login card.</summary>
    protected override void OnActionFailure(string action, Exception caught)
    {
        if (caught is System.Data.Common.DbException or HttpRequestException or TimeoutException)
        {
            App.Unexpected($"During execution of {action} action", caught); // the detail, for the log
            Scope.ErrorMessage = CommunicationFailure;
            return;
        }

        base.OnActionFailure(action, caught);
    }
}
