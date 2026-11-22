using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Root;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Login;

/// <summary>Public place: asks for the credentials and, when they match, opens the session and goes home.</summary>
public sealed class LoginPresenter(RootPresenter app) : CubePresenter<RootPresenter, LoginScope>(app, new LoginScope())
{
    public const string NotRecognized = "Usuário ou senha não reconhecido!";
    public const string CommunicationFailure = "Falha de comunicação com o servidor. Verifique sua conexão.";

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
