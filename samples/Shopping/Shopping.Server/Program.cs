using Shopping.Persistence;
using Shopping.Server;

var builder = WebApplication.CreateBuilder(args);

var database = builder.Configuration.GetSection("Shopping:Database");
var store = ShoppingStore.Open(
    database.GetValue("Provider", DatabaseProvider.PostgreSql),
    database["ConnectionString"] ?? throw new InvalidOperationException("Shopping:Database:ConnectionString is missing."),
    database["Schema"] ?? "shopping");

using var startupLogs = LoggerFactory.Create(l => l.AddConsole());
var auth = ShoppingApi.AuthOptions(builder.Configuration, builder.Environment, startupLogs.CreateLogger("Shopping.Server"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddShoppingApi(store, auth);

var app = builder.Build();

if (await store.EnsureCreated())
{
    app.Logger.LogInformation("Shopping schema and demo data created ({Provider}).", store.Provider);
}

app.MapShoppingApi();

await app.RunAsync();
await store.DisposeAsync();

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
