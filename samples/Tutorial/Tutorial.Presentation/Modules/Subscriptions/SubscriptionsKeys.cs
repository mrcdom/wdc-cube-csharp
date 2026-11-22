using Tutorial.Presentation.Modules.Main;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Subscriptions;

/// <summary>The list has no names of its own; its detail dialog extends these keys.</summary>
public class SubscriptionsKeys : MainKeys
{
    public new const string Prefix = "sb";

    public static new Place Place { get; } =
        MainKeys.Place.Child<MainPresenter>("subscriptions", app => new SubscriptionsPresenter(app));

    public SubscriptionsKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected SubscriptionsKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }
}
