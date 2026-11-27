using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyCubeApp.Blazor.Components.Pages;
using MyCubeApp.Blazor.Views;
using Wdc.Cube.Blazor;

namespace MyCubeApp.Tests;

/// <summary>The page on Blazor's renderer: the real application and views, on a throwaway schema.</summary>
public sealed class ShellTests : IAsyncDisposable
{
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    private readonly BunitContext _ctx = new();

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    private async Task<(IRenderedComponent<Shell> Page, TestDatabase Db)> Open(string address = "")
    {
        var db = await TestDatabase.Create();
        _ctx.Services.AddSingleton(db.Tasks);
        _ctx.Services.AddCubeViews(ViewCatalog.Register);
        if (address.Length > 0)
        {
            _ctx.Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        }

        return (_ctx.Render<Shell>(), db);
    }

    private string Address
    {
        get
        {
            var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
            return navigation.ToBaseRelativePath(navigation.Uri);
        }
    }

    [Fact]
    public async Task TheList_OpensATaskBesideIt()
    {
        var (page, db) = await Open();
        await using var _ = db;

        page.WaitForAssertion(() =>
        {
            Assert.Equal(3, page.FindAll(".task").Count);
            Assert.Equal("2 left", page.Find(".remaining").TextContent);
        }, s_wait);

        await page.InvokeAsync(() => page.FindAll(".task .open")[1].Click());

        page.WaitForAssertion(() => Assert.StartsWith("tasks/task?tk-id=", Address), s_wait);
        Assert.Equal("Open a task, rename it and save", page.Find(".detail .rename input").GetAttribute("value"));
    }

    [Fact]
    public async Task AddingNoTitle_ShowsTheMessage()
    {
        var (page, db) = await Open();
        await using var _ = db;
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".task").Count), s_wait);

        await page.InvokeAsync(() => page.Find("form.add").Submit());

        page.WaitForAssertion(() => Assert.Equal("Type what there is to do.", page.Find(".list .error").TextContent), s_wait);
    }
}
