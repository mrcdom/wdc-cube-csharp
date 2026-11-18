using Tutorial.Blazor.Components;
using Tutorial.Blazor.Views;
using Tutorial.Presentation.Services;
using Wdc.Cube.Blazor;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<ITutorialService, TutorialService>(); // one per circuit, like the application
builder.Services.AddCubeViews(ViewCatalog.Register);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
