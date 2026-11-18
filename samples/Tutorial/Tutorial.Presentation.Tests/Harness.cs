using Microsoft.Extensions.Time.Testing;
using Tutorial.Presentation.Modules.Main;
using Tutorial.Presentation.Services;
using Wdc.Cube;
using Wdc.Cube.Testing;

namespace Tutorial.Presentation.Tests;

/// <summary>Starts the tutorial at an address, with no view: the presentation is tested on its own.</summary>
internal sealed class Harness
{
    private Harness(string address, ITutorialService service)
    {
        History = new TestHistoryManager(address);
        Service = service;
        App = new MainPresenter(History, service, timeProvider: Time);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));

    public TestHistoryManager History { get; }

    public ITutorialService Service { get; }

    public MainPresenter App { get; }

    public MainScope Scope => App.Scope;

    public static async Task<Harness> Start(string address = "", ITutorialService? service = null)
    {
        var harness = new Harness(address, service ?? new TutorialService());
        await harness.App.KickStart(Places.Main);
        await harness.App.Settle();
        return harness;
    }

    public T PresenterOf<T>(Place place)
        where T : class, ICubePresenter => App.PresenterOf<T>(place);

    /// <summary>Runs on the dispatcher (where a view would act) and settles.</summary>
    public async Task On(Action action)
    {
        await App.InvokeAsync(action);
        await App.Settle();
    }

    /// <summary>Calls a scope action (as a view would) and settles.</summary>
    public async Task Do(Func<Task> action)
    {
        await action();
        await App.Settle();
    }
}

/// <summary>A service whose answers the test releases, to put requests in flight and make them cross.</summary>
internal sealed class GatedService : ITutorialService
{
    private readonly TutorialService _inner = new();

    public Dictionary<long, TaskCompletionSource> SiteGates { get; } = [];

    public TaskCompletionSource? SitesGate { get; set; }

    public async Task<IReadOnlyList<SiteItem>> FetchSubscribableSites()
    {
        if (SitesGate is { } gate)
        {
            await gate.Task;
        }

        return await _inner.FetchSubscribableSites();
    }

    public async Task<SiteItem> FetchSiteItem(long siteId)
    {
        if (SiteGates.TryGetValue(siteId, out var gate))
        {
            await gate.Task;
        }

        return await _inner.FetchSiteItem(siteId);
    }

    public Task UpdateOrAddSiteSubscription(long siteId, string email) => _inner.UpdateOrAddSiteSubscription(siteId, email);

    public Task<IReadOnlyList<TodoItem>> FetchTodos(long userId) => _inner.FetchTodos(userId);
}
