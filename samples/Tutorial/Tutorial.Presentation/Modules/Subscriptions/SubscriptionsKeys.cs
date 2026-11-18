using Tutorial.Presentation.Modules.Main;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Subscriptions;

public sealed class SubscriptionsKeys(CubeApplication app, FlipIntent? intent = null)
    : MainKeys(app, intent, Places.Subscriptions);
