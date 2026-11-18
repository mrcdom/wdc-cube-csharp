using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Shopping.Persistence;
using Wdc.Domain;
using Wdc.Rest.Server;

namespace Shopping.Server;

/// <summary>
/// The Shopping API: the four repositories as REST routes (decision D-18), remote transactions (D-17), JWT login
/// (D-19), the product image, and the rules of who reaches what.
/// </summary>
public static class ShoppingApi
{
    public static IServiceCollection AddShoppingApi(this IServiceCollection services, ShoppingStore store, ShoppingAuthOptions auth)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(auth);
        services.AddSingleton(store);
        services.AddSingleton(store.Repositories);
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
        MapRepositories(app);
        return app;
    }

    // :: Who reaches what

    private static void MapRepositories(WebApplication app)
    {
        var repositories = app.Services.GetRequiredService<ShoppingRepositories>();

        new RepositoryRpc<User, UserCriteria, long>("/api/repo/user", User.Model, () => new UserCriteria(), _ => repositories.Users)
        {
            Authorize = Allow("user"),
            Scope = (c, criteria) => Reach(c, id => criteria.UserId.Restrict(id)),
            RestrictToId = (criteria, id) => criteria.UserId.Restrict(id),
            SanitizeProjection = (_, p) => p.Password = null,
            SanitizeOutput = (_, u) => u.Password = null,
            BeforeInsert = (_, u) =>
            {
                u.Password = u.Password is null ? null : PasswordHash.Hash(u.Password);
                return ValueTask.CompletedTask;
            },
            BeforeUpdate = (_, u, sent) =>
            {
                if (sent.Password is not null && u.Password is not null)
                {
                    u.Password = PasswordHash.Hash(u.Password);
                }

                return ValueTask.CompletedTask;
            },
        }.Map(app);

        new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model, () => new ProductCriteria(),
            _ => repositories.Products)
        {
            Authorize = Allow("product"),
        }.Map(app);

        new RepositoryRpc<Purchase, PurchaseCriteria, long>("/api/repo/purchase", Purchase.Model, () => new PurchaseCriteria(),
            _ => repositories.Purchases)
        {
            Authorize = Allow("purchase"),
            Scope = (c, criteria) => Reach(c, id => criteria.UserId.Restrict(id)),
            RestrictToId = (criteria, id) => criteria.PurchaseId.Restrict(id),
            SanitizeProjection = (_, p) => DropPassword(p.User),
            SanitizeOutput = (_, p) => DropPassword(p.User),
            BeforeInsert = async (c, purchase) =>
            {
                if (Permissions.ReachOf(c.User) is { } owner)
                {
                    purchase.User = new User { Id = owner }; // bought by whoever is logged in, whatever was sent
                }

                foreach (var item in purchase.Items ?? [])
                {
                    item.Price = await PriceOf(repositories, item.Product?.Id, c.RequestAborted);
                }
            },
            BeforeUpdate = (c, _, sent) => sent.User is not null && Permissions.ReachOf(c.User) is not null
                ? throw new AccessDeniedException("A purchase cannot change hands.")
                : ValueTask.CompletedTask,
        }.Map(app);

        new RepositoryRpc<PurchaseItem, PurchaseItemCriteria, long>("/api/repo/purchase-item", PurchaseItem.Model,
            () => new PurchaseItemCriteria(), _ => repositories.PurchaseItems)
        {
            Authorize = Allow("purchase-item"),
            Scope = (c, criteria) => Reach(c, id => criteria.UserId.Restrict(id)),
            RestrictToId = (criteria, id) => criteria.PurchaseItemId.Restrict(id),
            SanitizeProjection = (_, i) => DropPassword(i.Purchase?.User),
            SanitizeOutput = (_, i) => DropPassword(i.Purchase?.User),
            BeforeInsert = async (c, item) =>
            {
                await RequireOwnPurchase(c, repositories, item.Purchase?.Id);
                item.Price = await PriceOf(repositories, item.Product?.Id, c.RequestAborted);
            },
            BeforeUpdate = async (c, item, sent) =>
            {
                if (sent.Purchase is not null)
                {
                    await RequireOwnPurchase(c, repositories, item.Purchase?.Id);
                }

                if (sent.Price is not null && Permissions.ReachOf(c.User) is not null)
                {
                    throw new AccessDeniedException("The price of an item comes from the product.");
                }
            },
        }.Map(app);
    }

    private static Func<HttpContext, RpcOperation, ValueTask> Allow(string entity) => (context, operation) =>
    {
        Permissions.Require(context.User, entity, operation);
        return ValueTask.CompletedTask;
    };

    private static void Reach(HttpContext context, Action<long> restrict)
    {
        if (Permissions.ReachOf(context.User) is { } owner)
        {
            restrict(owner);
        }
    }

    private static async Task RequireOwnPurchase(HttpContext context, ShoppingRepositories repositories, long? purchaseId)
    {
        if (purchaseId is not { } id)
        {
            throw new InvalidRequestException("An item needs its purchase.");
        }

        var criteria = new PurchaseCriteria().PurchaseId.Restrict(id);
        Reach(context, owner => criteria.UserId.Restrict(owner));
        if (await repositories.Purchases.Count(criteria, context.RequestAborted) == 0)
        {
            throw new AccessDeniedException("Not your purchase.");
        }
    }

    private static async Task<decimal?> PriceOf(ShoppingRepositories repositories, long? productId, CancellationToken cancellationToken)
    {
        var product = productId is { } id
            ? await repositories.Products.FetchById(id, new Product { Price = Mark.Decimal }, cancellationToken)
            : null;
        return product?.Price ?? throw new InvalidRequestException($"No product {productId}.");
    }

    private static void DropPassword(User? user)
    {
        if (user is not null)
        {
            user.Password = null;
        }
    }

    // :: Routes outside the repositories

    /// <summary>200 with the PNG, 204 without an image, 404 for an unknown product. Revalidated, never "immutable".</summary>
    private static async Task<IResult> ProductImage(long productId, ShoppingRepositories repositories, HttpContext context)
    {
        try
        {
            var image = await repositories.Products.FetchImage(productId, context.RequestAborted);
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
