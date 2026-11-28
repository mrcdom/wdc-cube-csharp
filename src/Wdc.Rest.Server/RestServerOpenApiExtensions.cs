using Microsoft.AspNetCore.OpenApi;

namespace Wdc.Rest.Server;

/// <summary>The OpenAPI description of the routes of this package.</summary>
public static class RestServerOpenApiExtensions
{
    /// <summary>
    /// Adds to the document the routes of every <see cref="RepositoryRpc{TEntity,TCriteria,TKey}"/> and of the remote
    /// transactions mapped in the application, with the schemas read from the entity models and criteria: the entity,
    /// its projection, its criteria (field objects, operators, orderings), the query and the answers.
    /// </summary>
    /// <example><code>builder.Services.AddOpenApi(o =&gt; o.AddCubeRest());</code></example>
    public static OpenApiOptions AddCubeRest(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AddDocumentTransformer(new RestOpenApiTransformer());
    }
}
