using Shopping.Blazor.Components;
using Shopping.Blazor.Infra;
using Shopping.Blazor.Views;
using Shopping.Domain;
using Shopping.Persistence;
using Shopping.Presentation.Infra;
using Wdc.Cube.Blazor;

var builder = WebApplication.CreateBuilder(args);

var database = builder.Configuration.GetSection("Shopping:Database");
var store = ShoppingStore.Open(
    database.GetValue("Provider", DatabaseProvider.PostgreSql),
    database["ConnectionString"] ?? throw new InvalidOperationException("Shopping:Database:ConnectionString is missing."),
    database["Schema"] ?? "shopping");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(store.Repositories);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISessionStore, BrowserSessionStore>(); // one per circuit, like the application
builder.Services.AddCubeViews(ViewCatalog.Register);

var app = builder.Build();

if (await store.EnsureCreated())
{
    app.Logger.LogInformation("Shopping schema and demo data created ({Provider}).", store.Provider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();

// Product images, as the Kotlin host served them: 200 with the PNG, 204 without an image, 404 for an unknown product.
app.MapGet("/image/product/{productId:long}.png", async (long productId, ShoppingRepositories repositories) =>
{
    try
    {
        var image = await repositories.Products.FetchImage(productId);
        return image is null ? Results.NoContent() : Results.File(image, "image/png");
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();
await store.DisposeAsync();

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
