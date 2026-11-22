using Shopping.Presentation.Modules.Main;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Login;

/// <summary>The login has no names of its own; it sits outside the session guard.</summary>
public sealed class LoginKeys : MainKeys
{
    public new const string Prefix = "lg";

    public static new Place Place { get; } = MainKeys.Place.Child<MainPresenter>("login", app => new LoginPresenter(app));

    public LoginKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }
}
