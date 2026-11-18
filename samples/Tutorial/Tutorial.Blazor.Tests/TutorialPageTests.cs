using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tutorial.Blazor.Components.Pages;
using Tutorial.Blazor.Views;
using Tutorial.Presentation.Services;
using Wdc.Cube.Blazor;

namespace Tutorial.Blazor.Tests;

/// <summary>
/// The whole tutorial page on Blazor's renderer: the real application, views and address. Clicks go through the
/// scope actions, the screen and the address follow.
/// </summary>
public sealed class TutorialPageTests : IDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly TutorialService _service = new();

    public TutorialPageTests()
    {
        _ctx.Services.AddSingleton<ITutorialService>(_service);
        _ctx.Services.AddCubeViews(ViewCatalog.Register);
    }

    private NavigationManager Navigation => _ctx.Services.GetRequiredService<NavigationManager>();

    private string Address => Navigation.ToBaseRelativePath(Navigation.Uri);

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<Shell> Open(string address = "")
    {
        if (address.Length > 0)
        {
            Navigation.NavigateTo(address);
        }

        var page = _ctx.Render<Shell>();
        page.WaitForElement(".menu");
        return page;
    }

    private static void Click(IRenderedComponent<Shell> page, string selector, string text) =>
        page.FindAll(selector).Single(e => e.TextContent.Trim() == text).Click();

    [Fact]
    public void StartsOnTheHomeBody()
    {
        var page = Open();

        page.WaitForAssertion(() => Assert.Contains("Cube tutorial", page.Find("h1").TextContent, StringComparison.Ordinal));
        Assert.Equal(string.Empty, Address);
    }

    [Fact]
    public void Menu_OpensTheToDos_AndTheAddressFollows()
    {
        var page = Open();

        Click(page, ".menu button", "To-dos");

        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".todo-list li").Count));
        Assert.Equal("todos", Address);
        Assert.Equal("2", page.Find(".todo-count strong").TextContent);
    }

    [Fact]
    public void ToDos_TypingAndEnter_AddsAnItem_AndTheFooterCounts()
    {
        var page = Open("todos");
        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".todo-list li").Count));

        var input = page.Find(".new-todo");
        input.Input("Buy milk");
        input.KeyDown("Enter");

        page.WaitForAssertion(() => Assert.Equal(6, page.FindAll(".todo-list li").Count));
        page.WaitForAssertion(() => Assert.Equal("3", page.Find(".todo-count strong").TextContent));
        Assert.Contains("Buy milk", page.FindAll(".todo-list li label").Select(l => l.TextContent));
    }

    [Fact]
    public void ToDos_Filter_IsInTheAddress_AndARowToggleRedrawsTheCount()
    {
        var page = Open("todos?tm-showing=1");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".todo-list li").Count)); // from the address

        page.FindAll(".todo-list li .toggle")[0].Change(true);

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".todo-list li")));
        page.WaitForAssertion(() => Assert.Equal("1", page.Find(".todo-count strong").TextContent));

        Click(page, ".filters button", "All");
        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".todo-list li").Count));
        Assert.Equal("todos", Address);
    }

    [Fact]
    public void Subscriptions_DialogOpensOverTheList_SubscribesAndCloses()
    {
        var page = Open();
        Click(page, ".menu button", "Subscriptions");
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".site").Count));

        Click(page, ".site", "twitter.com");
        page.WaitForAssertion(() => Assert.Contains("twitter.com", page.Find(".dialog h3").TextContent, StringComparison.Ordinal));
        Assert.Equal("subscriptions/detail?sd-site=2", Address);
        Assert.Equal(3, page.FindAll(".site").Count); // the list stays beneath

        page.Find(".dialog .email").Input("ana@example.com");
        page.Find(".dialog .subscribe").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".dialog")));
        Assert.Equal("subscriptions", Address);
        Assert.Equal(["ana@example.com"], _service.SubscribersOf(2));
    }

    [Fact]
    public void Alert_ShowsAndCloses()
    {
        var page = Open();
        page.WaitForElement(".home");

        Click(page, ".home button", "Warning");
        page.WaitForAssertion(() => Assert.Contains("alert-warning", page.Find(".alert").ClassName, StringComparison.Ordinal));

        page.Find(".alert .close").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".alert")));
    }

    [Fact]
    public void ChangingTheAddress_FromOutside_BringsTheFace()
    {
        var page = Open("todos");
        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".todo-list li").Count));

        Navigation.NavigateTo("subscriptions/detail?sd-site=3"); // Back, a pasted link

        page.WaitForAssertion(() => Assert.Contains("gettr.com", page.Find(".dialog h3").TextContent, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".todo-list"));
    }
}
