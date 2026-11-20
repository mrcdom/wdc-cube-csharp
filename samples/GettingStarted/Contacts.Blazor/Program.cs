using Contacts;
using Contacts.Blazor.Components;
using Contacts.Blazor.Views;
using Wdc.Cube.Blazor;

// <snippet:program>
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<IContactBook, ContactBook>();
builder.Services.AddCubeViews(ViewCatalog.Register); // which component draws each scope

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
// </snippet:program>
