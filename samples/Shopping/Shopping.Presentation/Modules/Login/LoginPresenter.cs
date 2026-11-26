using Shopping.Presentation.Modules.Home;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Login;

/// <summary>Public place: asks for the credentials and, when they match, opens the session and goes home.</summary>
public sealed class LoginPresenter(RootPresenter app) : CubePresenter<RootPresenter, LoginScope>(app, new LoginScope())
{
    public const string NotRecognized = "Username or password not recognized!";
    public const string CommunicationFailure = "Could not reach the server. Check your connection.";

    /// <summary>The message after too many failures: how long to wait, when known.</summary>
    public static string TooManyAttempts(TimeSpan? wait) => wait is { } w
        ? $"Too many failed attempts. Try again in {Math.Max(1, (int)Math.Ceiling(w.TotalSeconds))} seconds."
        : "Too many failed attempts. Try again later.";

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
        catch (Exception caught) when (caught is System.Data.Common.DbException or HttpRequestException or TimeoutException)
        {
            App.Unexpected("Logging in", caught);
            Scope.ErrorMessage = CommunicationFailure;
        }
        finally
        {
            if (!IsReleased)
            {
                Scope.Loading = false;
            }
        }
    }
}
