using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Security;
using Shopping.Domain.Users;
using Shopping.Persistence;
using Wdc.Domain;
using Wdc.Rest.Server;

namespace Shopping.Server;

/// <summary>
/// The Shopping API: the repositories as REST routes, remote transactions, JWT login
/// and the product image. Who reaches what is in the policies of the protected repositories, the same
/// in every host; here, the actor comes from the request's token.
/// </summary>
public static class ShoppingApi
{
    public static IServiceCollection AddShoppingApi(this IServiceCollection services, ShoppingStore store, ShoppingAuthOptions auth)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(auth);
        services.AddSingleton(store);
        services.AddHttpContextAccessor();
        // What each request reaches: the repositories protected for whoever its token authenticated.
        services.AddScoped(sp => ShoppingProtection.Protect(store.Repositories,
            new ActorSource(sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User is { } user
                ? ShoppingTokens.ActorOf(user)
                : null)));
        services.AddSingleton<ITransactionService>(store.Database);
        services.AddSingleton<IDetachedTransactionSource>(store.Database);
        services.AddSingleton(auth);
        services.AddSingleton<ShoppingTokens>();
        services.AddCubeRest();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; // keep "sub" and "role" as issued
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = auth.Issuer,
                ValidAudience = auth.Issuer,
                IssuerSigningKey = auth.SigningKey,
                RoleClaimType = ShoppingTokens.RoleClaim,
                NameClaimType = "name",
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            o.Events = new JwtBearerEvents
            {
                // Our own 401 body ({"error":…}) comes from the guard below, not from the challenge.
                OnChallenge = c =>
                {
                    c.HandleResponse();
                    return Task.CompletedTask;
                },
            };
        });
        return services;
    }

    public static WebApplication MapShoppingApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseAuthentication();
        app.Use(RequireAuthentication);
        app.MapShoppingAuth();
        app.MapRemoteTransactions();
        app.MapGet("/api/repo/product/{productId:long}/image", ProductImage);
        app.MapGet("/image/product/{productId:long}.png", ProductImage); // the address the presentation hands out
        MapRepositories(app);
        return app;
    }

    // :: The repositories (who reaches what is in their policies, Shopping.Domain.Security)

    private static void MapRepositories(WebApplication app)
    {
        new RepositoryRpc<User, UserCriteria, long>("/api/repo/user", User.Model, () => new UserCriteria(),
            c => Of(c).Users).Map(app);
        new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model, () => new ProductCriteria(),
            c => Of(c).Products).Map(app);
        new RepositoryRpc<Purchase, PurchaseCriteria, long>("/api/repo/purchase", Purchase.Model, () => new PurchaseCriteria(),
            c => Of(c).Purchases).Map(app);
        new RepositoryRpc<PurchaseItem, PurchaseItemCriteria, long>("/api/repo/purchase-item", PurchaseItem.Model,
            () => new PurchaseItemCriteria(), c => Of(c).PurchaseItems).Map(app);
        new RepositoryRpc<PurchaseAmendment, PurchaseAmendmentCriteria, long>("/api/repo/purchase-amendment",
            PurchaseAmendment.Model, () => new PurchaseAmendmentCriteria(), c => Of(c).Amendments).Map(app);
        new RepositoryRpc<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria, long>("/api/repo/purchase-amendment-line",
            PurchaseAmendmentLine.Model, () => new PurchaseAmendmentLineCriteria(), c => Of(c).AmendmentLines).Map(app);
    }

    private static ShoppingRepositories Of(HttpContext context) => context.RequestServices.GetRequiredService<ShoppingRepositories>();

    // :: Routes outside the repositories

    /// <summary>200 with the PNG, 204 without an image, 404 for an unknown product. Revalidated, never "immutable".</summary>
    private static async Task<IResult> ProductImage(long productId, ShoppingStore store, HttpContext context)
    {
        try
        {
            var image = await store.Repositories.Products.FetchImage(productId, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-cache";
            return image is null ? Results.NoContent() : Results.File(image, "image/png");
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    /// <summary>Everything under <c>/api</c> needs a session, except login and the product images.</summary>
    private static async Task RequireAuthentication(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path;
        var open = path.StartsWithSegments("/api/auth")
                   || (HttpMethods.IsGet(context.Request.Method) && path.StartsWithSegments("/api/repo/product")
                       && path.Value!.EndsWith("/image", StringComparison.Ordinal));
        if (path.StartsWithSegments("/api") && !open && context.User.Identity?.IsAuthenticated != true)
        {
            await RestHttp.WriteError(context, new AuthenticationException("Authentication required."));
            return;
        }

        await next(context);
    }

    /// <summary>The signing key from configuration (Base64), or a random one in development.</summary>
    public static ShoppingAuthOptions AuthOptions(IConfiguration configuration, IHostEnvironment environment, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var section = configuration.GetSection("Shopping:Jwt");
        var options = new ShoppingAuthOptions { Issuer = section["Issuer"] ?? "wdc-shopping" };
        if (configuration["Shopping:History:Key"] is { Length: > 0 } historyKey)
        {
            options.HistoryMasterKey = Convert.FromBase64String(historyKey);
        }
        else if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Shopping:History:Key is missing (Base64, 32 bytes).");
        }
        if (section["Key"] is { Length: > 0 } key)
        {
            options.Key = Convert.FromBase64String(key);
        }
        else if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Shopping:Jwt:Key is missing (Base64, 32 bytes or more).");
        }
        else
        {
            logger.LogWarning("Shopping:Jwt:Key is not set: tokens are signed with a random key and die with the process.");
        }

        return options;
    }
}
