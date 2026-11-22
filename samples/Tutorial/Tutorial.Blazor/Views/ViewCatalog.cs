using Tutorial.Presentation.Modules;
using Tutorial.Presentation.Modules.Restricted;
using Tutorial.Presentation.Modules.Subscriptions;
using Tutorial.Presentation.Modules.TodoMvc;
using Wdc.Cube.Blazor;

namespace Tutorial.Blazor.Views;

/// <summary>Which component draws each scope that can land in a slot.</summary>
public static class ViewCatalog
{
    public static void Register(ViewRegistry views)
    {
        ArgumentNullException.ThrowIfNull(views);
        views
            .Register<RootScope, RootView>()
            .Register<BodyScope, BodyView>()
            .Register<AlertScope, AlertView>()
            .Register<RestrictedScope, RestrictedView>()
            .Register<SubscriptionsScope, SubscriptionsView>()
            .Register<SubscriptionsDetailScope, SubscriptionsDetailView>()
            .Register<TodoMvcScope, TodoMvcView>();
    }
}
