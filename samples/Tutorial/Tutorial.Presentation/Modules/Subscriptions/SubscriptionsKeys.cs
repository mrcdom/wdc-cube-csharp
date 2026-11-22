using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Subscriptions;

/// <summary>The list has no names of its own; its detail dialog extends these keys.</summary>
public class SubscriptionsKeys : RootKeys
{
    public new const string Prefix = "sb";

    public static new Place Place { get; } =
        RootKeys.Place.Child<RootPresenter>("subscriptions", app => new SubscriptionsPresenter(app));

    public SubscriptionsKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected SubscriptionsKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }
}
