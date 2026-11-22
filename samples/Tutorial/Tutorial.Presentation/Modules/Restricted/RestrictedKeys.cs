using Tutorial.Presentation.Modules.Root;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Restricted;

/// <summary>The restricted area has no names of its own.</summary>
public sealed class RestrictedKeys : RootKeys
{
    public new const string Prefix = "rs";

    public static new Place Place { get; } =
        RootKeys.Place.Child<RootPresenter>("restricted", app => new RestrictedPresenter(app));

    public RestrictedKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }
}
