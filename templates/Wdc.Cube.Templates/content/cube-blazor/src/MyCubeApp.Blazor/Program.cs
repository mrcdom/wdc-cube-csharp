using MyCubeApp.Blazor.Components;
using MyCubeApp.Blazor.Data;
using MyCubeApp.Blazor.Views;
using Wdc.Cube.Blazor;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Tasks") is { Length: > 0 } configured
    ? configured
    : throw new InvalidOperationException("ConnectionStrings:Tasks is missing (see README.md).");
var store = TaskStore.Open(connectionString, builder.Configuration["Database:Schema"] ?? "tasks");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(store.Tasks);
builder.Services.AddCubeViews(ViewCatalog.Register); // which component draws each scope

var app = builder.Build();

if (await store.EnsureCreated())
{
    app.Logger.LogInformation("Created the tasks table, with a few tasks.");
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();
await store.DisposeAsync();
