using Tutorial.Presentation.Modules.Main;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Restricted;

/// <summary>The restricted area has no names of its own.</summary>
public sealed class RestrictedKeys : MainKeys
{
    public new const string Prefix = "rs";

    public static new Place Place { get; } =
        MainKeys.Place.Child<MainPresenter>("restricted", app => new RestrictedPresenter(app));

    public RestrictedKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }
}
