using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Shopping.Blazor.Views;
using Shopping.Client;
using Shopping.Presentation.Infra;
using Wdc.Cube.Blazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");

// The presentation runs here; the data and the login are on the server that served this page (same origin).
var client = new ShoppingClient(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(client.Repositories);
builder.Services.AddSingleton<IAuthentication>(client.Authentication);
builder.Services.AddSingleton<ISessionStore, SessionStorageStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddCubeViews(ViewCatalog.Register);

await builder.Build().RunAsync();

/// <summary>
/// The tab's <c>sessionStorage</c>: survives F5, goes away with the tab. Plain (the Server host's Data Protection does
/// not exist in the browser); what it keeps is the user's own session.
/// </summary>
internal sealed class SessionStorageStore(IJSRuntime js) : ISessionStore
{
    public async Task<string?> Get(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("sessionStorage.getItem", key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public Task Set(string key, string value) => Write("sessionStorage.setItem", key, value);

    public Task Remove(string key) => Write("sessionStorage.removeItem", key);

    // Storage refused (private mode, quota): the session lives on in memory; only an F5 would lose it.
    private async Task Write(string function, params object?[] arguments)
    {
        try
        {
            await js.InvokeVoidAsync(function, arguments);
        }
        catch (JSException)
        {
        }
    }
}
