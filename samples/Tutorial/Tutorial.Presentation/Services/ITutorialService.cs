namespace Tutorial.Presentation.Services;

/// <summary>The tutorial's server. Presenters take it as a plain reference, so a test can hand them a double.</summary>
public interface ITutorialService
{
    Task<IReadOnlyList<SiteItem>> FetchSubscribableSites();

    /// <exception cref="KeyNotFoundException">No site under <paramref name="siteId"/>.</exception>
    Task<SiteItem> FetchSiteItem(long siteId);

    Task UpdateOrAddSiteSubscription(long siteId, string email);

    /// <summary>0 is the sample list; a negative id the 1000-item generator; a positive id a user's list.</summary>
    Task<IReadOnlyList<TodoItem>> FetchTodos(long userId);
}
