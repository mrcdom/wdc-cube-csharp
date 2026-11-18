using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;
using Shopping.Presentation.Infra;

namespace Shopping.Blazor.Infra;

/// <summary>
/// The tab's session storage, encrypted by ASP.NET Data Protection: survives F5, goes away with the tab. Anything
/// unreadable (an old key, a tampered value) reads as absent.
/// </summary>
public sealed class BrowserSessionStore(ProtectedSessionStorage storage) : ISessionStore
{
    public async Task<string?> Get(string key)
    {
        try
        {
            var result = await storage.GetAsync<string>(key);
            return result.Success ? result.Value : null;
        }
        catch (Exception caught) when (caught is CryptographicException or JSException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task Set(string key, string value) => await storage.SetAsync(key, value);

    public async Task Remove(string key) => await storage.DeleteAsync(key);
}
