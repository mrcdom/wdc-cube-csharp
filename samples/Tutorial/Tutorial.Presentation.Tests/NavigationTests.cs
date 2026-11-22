using Tutorial.Presentation.Modules.Main;
using Tutorial.Presentation.Modules.Subscriptions;
using Tutorial.Presentation.Modules.TodoMvc;

namespace Tutorial.Presentation.Tests;

/// <summary>Navigation of the tutorial: menu, addresses, Back and the restricted area.</summary>
public sealed class NavigationTests
{
    [Fact]
    public async Task ColdStartOnADeepAddress_FlipsThereOnce()
    {
        var h = await Harness.Start("todos?tm-showing=1");

        Assert.IsType<TodoMvcScope>(h.Scope.Body);
        Assert.Equal(ShowingOptions.Active, h.PresenterOf<TodoMvcPresenter>(TodoMvcKeys.Place).Scope.Footer!.Showing);
        Assert.Equal(["todos?tm-showing=1"], h.History.Tokens);
    }

    [Fact]
    public async Task WithNoAddress_LandsOnTheHomeBody()
    {
        var h = await Harness.Start();

        Assert.IsType<BodyScope>(h.Scope.Body);
        Assert.Equal(0, h.App.PresenterCount);
    }

    [Fact]
    public async Task ANavAction_PutsTheModuleIntoTheBodySlot()
    {
        var h = await Harness.Start();

        await h.Do(h.Scope.OnOpenTodos);
        Assert.IsType<TodoMvcScope>(h.Scope.Body);

        await h.Do(h.Scope.OnHome);
        Assert.IsType<BodyScope>(h.Scope.Body);
    }

    [Fact]
    public async Task StartsInsideAModule_WhenTheAddressNamesOne()
    {
        var h = await Harness.Start("subscriptions");

        Assert.IsType<SubscriptionsScope>(h.Scope.Body);
    }

    [Fact]
    public async Task PublishesATokenForEachPlaceItPassesThrough()
    {
        var h = await Harness.Start();

        await h.Do(h.Scope.OnOpenTodos);
        await h.Do(h.Scope.OnOpenSubscriptions);
        await h.Do(h.Scope.OnHome);

        Assert.Equal(["todos", "subscriptions", ""], h.History.Tokens); // the root's address is empty
    }

    [Fact]
    public async Task SwapsTheBodyScope_RatherThanKeepingBothModulesAlive()
    {
        var h = await Harness.Start();
        await h.Do(h.Scope.OnOpenTodos);
        var todos = h.PresenterOf<TodoMvcPresenter>(TodoMvcKeys.Place);

        await h.Do(h.Scope.OnOpenSubscriptions);

        Assert.True(todos.IsReleased);
        Assert.Null(h.App.GetPresenter(TodoMvcKeys.Place));
        Assert.IsType<SubscriptionsScope>(h.Scope.Body);
    }

    [Fact]
    public async Task Alert_CarriesTheSeverityAskedFor_AndClearsOnClose()
    {
        var h = await Harness.Start();
        var body = Assert.IsType<BodyScope>(h.Scope.Body);

        await h.Do(() => body.OnOpenAlert(AlertSeverity.Warning));
        var alert = h.Scope.Alert!;
        Assert.Equal(AlertSeverity.Warning, alert.Severity);

        await h.Do(alert.OnClose);
        Assert.Null(h.Scope.Alert);
    }

    [Fact]
    public async Task AnUnimplementedAction_IsReportedAsAnAlert_NotANavigation()
    {
        var h = await Harness.Start();

        await h.Do(h.Scope.OnLogin);

        Assert.Equal(AlertSeverity.Info, h.Scope.Alert!.Severity);
        Assert.IsType<BodyScope>(h.Scope.Body);
        Assert.Equal([""], h.History.Tokens);
    }
}
