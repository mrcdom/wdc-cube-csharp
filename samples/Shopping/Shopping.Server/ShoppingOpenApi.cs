using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Wdc.Rest.Server;

namespace Shopping.Server;

/// <summary>
/// The API's description (<c>/openapi/v1.json</c>) and a page to try it (<c>/scalar</c>), in development only: the
/// repository and transaction routes come from Cube, the bearer token from here.
/// </summary>
public static class ShoppingOpenApi
{
    /// <summary>The name of the security scheme of the access token.</summary>
    public const string BearerScheme = "Bearer";

    public static IServiceCollection AddShoppingOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(o => o.AddCubeRest().AddDocumentTransformer(DescribeShopping));

    /// <summary>Maps the description and its page when the host runs in development; otherwise, nothing.</summary>
    public static WebApplication MapShoppingOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        return app;
    }

    private static Task DescribeShopping(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Shopping API",
            Version = "v1",
            Description = "Everything under /api needs an access token from /api/auth/login, except the sign-in routes "
                + "and the product images.",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "The access token of a session (15 minutes; /api/auth/refresh gives a new one).",
        };
        var bearer = new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [] };
        foreach (var (path, item) in document.Paths)
        {
            if (!path.StartsWith("/api/", StringComparison.Ordinal) || item.Operations is null)
            {
                continue;
            }

            foreach (var (method, operation) in item.Operations)
            {
                var open = path.StartsWith("/api/auth/", StringComparison.Ordinal)
                           || (method == HttpMethod.Get && path.EndsWith("/image", StringComparison.Ordinal));
                if (!open)
                {
                    operation.Security = [bearer];
                }
            }
        }

        return Task.CompletedTask;
    }
}
