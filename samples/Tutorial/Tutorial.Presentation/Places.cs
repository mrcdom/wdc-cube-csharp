using Tutorial.Presentation.Modules.Main;
using Tutorial.Presentation.Modules.Restricted;
using Tutorial.Presentation.Modules.Subscriptions;
using Tutorial.Presentation.Modules.TodoMvc;
using Wdc.Cube;

namespace Tutorial.Presentation;

/// <summary>The cube: every face the tutorial can show. The folder tree mirrors this tree.</summary>
public static class Places
{
    /// <summary>The application itself (the page frame).</summary>
    public static readonly Place Main = Place.Root;

    public static readonly Place Todos = Place.Create<MainPresenter>("todos", app => new TodoMvcPresenter(app));

    public static readonly Place Subscriptions =
        Place.Create<MainPresenter>("subscriptions", app => new SubscriptionsPresenter(app));

    /// <summary>A dialog over the list: a place below it, shown in the dialog slot.</summary>
    public static readonly Place SubscriptionsDetail =
        Subscriptions.Child<MainPresenter>("detail", app => new SubscriptionsDetailPresenter(app));

    public static readonly Place Restricted = Place.Create<MainPresenter>("restricted", app => new RestrictedPresenter(app));

    /// <summary>Every place an address can name.</summary>
    public static IReadOnlyList<Place> All { get; } = [Todos, Subscriptions, SubscriptionsDetail, Restricted];
}
