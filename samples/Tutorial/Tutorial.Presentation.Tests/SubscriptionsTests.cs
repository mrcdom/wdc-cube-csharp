using Tutorial.Presentation.Modules.Subscriptions;
using Tutorial.Presentation.Services;
using Wdc.Cube.Testing;

namespace Tutorial.Presentation.Tests;

/// <summary>The subscriptions list and its detail dialog.</summary>
public sealed class SubscriptionsTests
{
    private static SubscriptionsScope List(Harness h) => Assert.IsType<SubscriptionsScope>(h.Scope.Body);

    private static SubscriptionsDetailScope Dialog(Harness h) => Assert.IsType<SubscriptionsDetailScope>(h.Scope.Dialog);

    [Fact]
    public async Task ListsTheSitesTheServiceOffers()
    {
        var h = await Harness.Start("subscriptions");

        Assert.Equal(["youtube.com", "twitter.com", "gettr.com"], List(h).Sites.Select(s => s.Site));
    }

    [Fact]
    public async Task OpensTheDialogForTheSiteClicked_NamingItInTheAddress()
    {
        var h = await Harness.Start("subscriptions");

        await h.Do(() => List(h).OnItemClicked(List(h).Sites[1]));

        Assert.Equal("twitter.com", Dialog(h).Site);
        Assert.Equal("subscriptions/detail?sd-site=2", h.History.Token);
        Assert.IsType<SubscriptionsScope>(h.Scope.Body); // the list stays beneath the dialog
    }

    [Fact]
    public async Task RebuildsTheDialogFromTheAddressAlone()
    {
        var h = await Harness.Start("subscriptions/detail?sd-site=3");

        Assert.Equal("gettr.com", Dialog(h).Site);
        Assert.Equal(3, List(h).Sites.Count);
    }

    [Fact]
    public async Task OpensWithTheEmailFieldEmpty()
    {
        var h = await Harness.Start("subscriptions/detail?sd-site=1");

        Assert.Equal(string.Empty, Dialog(h).Email);
    }

    [Fact]
    public async Task Closing_ReturnsToTheList_WhenOpenedFromIt()
    {
        var h = await Harness.Start("subscriptions");
        await h.Do(() => List(h).OnItemClicked(List(h).Sites[0]));

        await h.Do(Dialog(h).OnClose);

        Assert.Null(h.Scope.Dialog);
        Assert.Equal("subscriptions", h.History.Token);
    }

    [Fact]
    public async Task Closing_ReturnsToTheList_WhenOpenedByAddress()
    {
        var h = await Harness.Start("subscriptions/detail?sd-site=1");

        await h.Do(Dialog(h).OnClose);

        Assert.Null(h.Scope.Dialog);
        Assert.IsType<SubscriptionsScope>(h.Scope.Body);
    }

    [Fact]
    public async Task Subscribing_FromAColdStart_RecordsAndReturnsToTheList()
    {
        var service = new TutorialService();
        var h = await Harness.Start("subscriptions/detail?sd-site=2", service);

        await h.Do(() => Dialog(h).OnEmailChanged("ana@example.com"));
        await h.Do(Dialog(h).OnSubscribe);

        Assert.Equal(["ana@example.com"], service.SubscribersOf(2));
        Assert.Null(h.Scope.Dialog);
        Assert.Equal("subscriptions", h.History.Token);
    }

    [Theory]
    [InlineData("", "Field required")]
    [InlineData("not-an-address", "Wrong value")]
    public async Task Validation_RefusesAndKeepsTheDialogOpen(string email, string title)
    {
        var h = await Harness.Start("subscriptions/detail?sd-site=1");

        await h.Do(() => Dialog(h).OnEmailChanged(email));
        await h.Do(Dialog(h).OnSubscribe);

        Assert.Equal(title, h.Scope.Alert!.Title);
        Assert.NotNull(h.Scope.Dialog);
        Assert.Equal(email, Dialog(h).Email); // what was typed stays in the box: the view draws the scope
        Assert.Equal("subscriptions/detail?sd-site=1", h.History.Token);
    }

    [Fact]
    public async Task Validation_LeavesTheModuleReachableAfterTheAlertIsDismissed()
    {
        var h = await Harness.Start("subscriptions/detail?sd-site=1");
        await h.Do(Dialog(h).OnSubscribe);

        await h.Do(h.Scope.Alert!.OnClose);
        await h.Do(Dialog(h).OnClose);

        Assert.Null(h.Scope.Alert);
        Assert.Null(h.Scope.Dialog);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task AnAddressTheDialogCannotUse_LandsOnTheList(string site)
    {
        var h = await Harness.Start($"subscriptions/detail?sd-site={site}");

        Assert.Null(h.Scope.Dialog);
        Assert.IsType<SubscriptionsScope>(h.Scope.Body);
        Assert.Equal("subscriptions", h.History.Token);
    }

    [Fact]
    public async Task ASiteTheServiceDoesNotKnow_ClosesAndSaysWhy()
    {
        var h = await Harness.Start("subscriptions/detail?sd-site=99");

        Assert.Null(h.Scope.Dialog);
        Assert.Equal("Unknown site", h.Scope.Alert!.Title);
    }

    [Fact]
    public async Task WhileARequestIsOut_TheListIsShownBeforeTheSitesArrive()
    {
        var service = new GatedService { SitesGate = new TaskCompletionSource() };
        var h = await Harness.Start("subscriptions", service);

        Assert.True(List(h).Loading);
        Assert.Empty(List(h).Sites);

        service.SitesGate.SetResult();
        await h.App.Settle();
        await WaitUntil(() => List(h).Sites.Count == 3);
        Assert.False(List(h).Loading);
    }

    [Fact]
    public async Task WhileARequestIsOut_TheDialogIsShownBeforeTheSiteArrives()
    {
        var service = new GatedService();
        service.SiteGates[2] = new TaskCompletionSource();
        var h = await Harness.Start("subscriptions/detail?sd-site=2", service);

        Assert.NotNull(h.Scope.Dialog);
        Assert.Null(Dialog(h).Site);

        service.SiteGates[2].SetResult();
        await WaitUntil(() => Dialog(h).Site is not null);
        Assert.Equal("twitter.com", Dialog(h).Site);
    }

    [Fact]
    public async Task WhenResponsesCross_TheSiteAskedForLastIsKept()
    {
        var service = new GatedService();
        service.SiteGates[1] = new TaskCompletionSource();
        service.SiteGates[2] = new TaskCompletionSource();
        var h = await Harness.Start("subscriptions/detail?sd-site=1", service);

        h.History.Navigate("subscriptions/detail?sd-site=2"); // the reader moved on before site 1 arrived
        await WaitUntil(() => h.History.Location == "subscriptions/detail?sd-site=2" && h.App.LastPlace == SubscriptionsDetailKeys.Place);
        await h.App.Settle();

        service.SiteGates[2].SetResult(); // the newer answer first...
        await WaitUntil(() => Dialog(h).Site == "twitter.com");
        service.SiteGates[1].SetResult(); // ...then the older one arrives late
        await h.App.Settle();
        await Task.Delay(20, TestContext.Current.CancellationToken);
        await h.App.Settle();

        Assert.Equal("twitter.com", Dialog(h).Site);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "Condition not reached in time.");
    }
}
