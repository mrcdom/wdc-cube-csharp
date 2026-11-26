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

    // Finds and acts on the renderer's thread: a render in between would replace the handlers of the element found.
    private static void On(IRenderedComponent<Shell> page, Action act) => page.InvokeAsync(act).GetAwaiter().GetResult();

    private static void Click(IRenderedComponent<Shell> page, string selector, string text) =>
        On(page, () => page.FindAll(selector).Single(e => e.TextContent.Trim() == text).Click());

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
    public void ToDos_DoubleClick_OpensTheEditorWithTheCaretInIt()
    {
        var page = Open("todos");
        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".todo-list li").Count));

        page.FindAll(".todo-list li label")[0].DoubleClick();

        page.WaitForAssertion(() => Assert.Equal("Walk the dog", page.Find(".todo-list li input.edit").GetAttribute("value")));
        _ctx.JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task ToDos_TypingAndEnter_AddsAnItem_AndTheFooterCounts()
    {
        var page = Open("todos");
        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".todo-list li").Count));

        // Find and fire in one go: @bind makes a new handler on every render, and a render may land in between.
        await page.InvokeAsync(() => page.Find(".new-todo").Input("Buy milk"));
        await page.InvokeAsync(() => page.Find(".new-todo").KeyDown("Enter"));

        page.WaitForAssertion(() => Assert.Equal(6, page.FindAll(".todo-list li").Count));
        page.WaitForAssertion(() => Assert.Equal("3", page.Find(".todo-count strong").TextContent));
        Assert.Contains("Buy milk", page.FindAll(".todo-list li label").Select(l => l.TextContent));
    }

    [Fact]
    public void ToDos_Filter_IsInTheAddress_AndARowToggleRedrawsTheCount()
    {
        var page = Open("todos?tm-showing=1");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".todo-list li").Count)); // from the address

        On(page, () => page.FindAll(".todo-list li .toggle")[0].Change(true));

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".todo-list li")));
        page.WaitForAssertion(() => Assert.Equal("1", page.Find(".todo-count strong").TextContent));

        Click(page, ".filters button", "All");
        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".todo-list li").Count));
        Assert.Equal("todos", Address);
    }

    [Fact]
    public async Task Subscriptions_DialogOpensOverTheList_SubscribesAndCloses()
    {
        var page = Open();
        Click(page, ".menu button", "Subscriptions");
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".site").Count));

        Click(page, ".site", "twitter.com");
        page.WaitForAssertion(() => Assert.Contains("twitter.com", page.Find(".dialog h3").TextContent, StringComparison.Ordinal));
        Assert.Equal("subscriptions/detail?sd-site=2", Address);
        Assert.Equal(3, page.FindAll(".site").Count); // the list stays beneath

        await page.InvokeAsync(() => page.Find(".dialog .email").Input("ana@example.com"));
        On(page, () => page.Find(".dialog .subscribe").Click());

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

        On(page, () => page.Find(".alert .close").Click());
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
