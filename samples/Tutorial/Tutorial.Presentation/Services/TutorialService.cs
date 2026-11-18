namespace Tutorial.Presentation.Services;

/// <summary>In memory. The TS tutorial fetched positive user ids from a public API; here they are generated.</summary>
public sealed class TutorialService : ITutorialService
{
    private static readonly SiteItem[] s_sites =
    [
        new(1, "youtube.com"),
        new(2, "twitter.com"),
        new(3, "gettr.com"),
    ];

    private readonly Dictionary<long, HashSet<string>> _subscriptions = [];

    public Task<IReadOnlyList<SiteItem>> FetchSubscribableSites() => Task.FromResult<IReadOnlyList<SiteItem>>(s_sites);

    public Task<SiteItem> FetchSiteItem(long siteId) =>
        Array.Find(s_sites, s => s.Id == siteId) is { } site
            ? Task.FromResult(site)
            : Task.FromException<SiteItem>(new KeyNotFoundException($"No item found under id={siteId}"));

    public Task UpdateOrAddSiteSubscription(long siteId, string email)
    {
        lock (_subscriptions)
        {
            if (!_subscriptions.TryGetValue(siteId, out var emails))
            {
                _subscriptions[siteId] = emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            emails.Add(email);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyCollection<string> SubscribersOf(long siteId)
    {
        lock (_subscriptions)
        {
            return _subscriptions.TryGetValue(siteId, out var emails) ? [.. emails] : [];
        }
    }

    public Task<IReadOnlyList<TodoItem>> FetchTodos(long userId)
    {
        IReadOnlyList<TodoItem> todos = userId switch
        {
            < 0 => [.. Enumerable.Range(0, 1000).Select(i => new TodoItem(i + 1, $"Item {i}", i % 2 == 0))],
            0 =>
            [
                new(1, "Walk the dog", false),
                new(2, "Write an app", true),
                new(3, "Go to school", false),
                new(4, "Watch Michael Reeves", true),
                new(5, "Add automatic deployment", true),
            ],
            _ => [.. Enumerable.Range(1, 10).Select(k => new TodoItem(k, $"User {userId} task {k}", k % 3 == 0))],
        };
        return Task.FromResult(todos);
    }
}
