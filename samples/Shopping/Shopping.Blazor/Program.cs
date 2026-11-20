using Shopping.Blazor.Components;
using Shopping.Blazor.Infra;
using Shopping.Blazor.Views;
using Shopping.Domain.Security;
using Shopping.Persistence;
using Shopping.Presentation.Infra;
using Wdc.Cube.Blazor;
using Wdc.Domain;

var builder = WebApplication.CreateBuilder(args);

var database = builder.Configuration.GetSection("Shopping:Database");
var store = ShoppingStore.Open(
    database.GetValue("Provider", DatabaseProvider.PostgreSql),
    database["ConnectionString"] ?? throw new InvalidOperationException("Shopping:Database:ConnectionString is missing."),
    database["Schema"] ?? "shopping");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(store);
// Each circuit is a session: its own actor, set by the login, and the repositories protected for it.
builder.Services.AddScoped<ActorSource>();
builder.Services.AddScoped(sp => ShoppingProtection.Protect(store.Repositories, sp.GetRequiredService<ActorSource>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISessionStore, BrowserSessionStore>(); // one per circuit, like the application

// Each user's address key derives from this master key; in Development a random one is enough.
var historyMaster = builder.Configuration["Shopping:History:Key"] is { Length: > 0 } historyKey
    ? Convert.FromBase64String(historyKey)
    : builder.Environment.IsDevelopment()
        ? System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
        : throw new InvalidOperationException("Shopping:History:Key is missing (Base64, 32 bytes).");
builder.Services.AddScoped<IAuthentication>(sp => new LocalAuthentication(store.Repositories,
    sp.GetRequiredService<ActorSource>(), id => Shopping.Domain.Users.HistoryKeys.Derive(historyMaster, id)));
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

// Product images: 200 with the PNG, 204 without an image, 404 for an unknown product.
app.MapGet("/image/product/{productId:long}.png", async (long productId, ShoppingStore images) =>
{
    try
    {
        var image = await images.Repositories.Products.FetchImage(productId);
        return image is null ? Results.NoContent() : Results.File(image, "image/png");
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Shopping.Blazor.Views.Pages.Shell).Assembly); // the pages live with the shared views

await app.RunAsync();
await store.DisposeAsync();

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
