using Tutorial.Presentation.Services;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Subscriptions;

public sealed class SubscriptionsPresenter(RootPresenter app)
    : CubePresenter<RootPresenter, SubscriptionsScope>(app, new SubscriptionsScope())
{
    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new SubscriptionsKeys(App, intent);
        keys.ParentSlot(Scope); // the screen appears before any wait

        if (initialization)
        {
            Scope.OnItemClicked = Action<SiteItem>(OnItemClicked);
            _ = LoadSites(); // in parallel: the flip does not wait for the network
        }

        if (last)
        {
            keys.DialogSlot(null); // back on the list: no dialog
        }

        return Task.FromResult(true);
    }

    private async Task LoadSites()
    {
        Scope.Loading = true;
        try
        {
            Scope.Sites = await App.Service.FetchSubscribableSites();
        }
        catch (Exception caught)
        {
            Unexpected("Loading the sites", caught);
        }
        finally
        {
            Scope.Loading = false;
        }
    }

    private Task OnItemClicked(SiteItem item)
    {
        var target = new SubscriptionsDetailKeys(App) { SiteId = item.Id, Item = item };
        return target.Flip();
    }
}
