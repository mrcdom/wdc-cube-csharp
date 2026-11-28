using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
#if NET11_0_OR_GREATER
using MediaTypes = System.Collections.Generic.Dictionary<string, Microsoft.OpenApi.IOpenApiMediaType>;
#else
using MediaTypes = System.Collections.Generic.Dictionary<string, Microsoft.OpenApi.OpenApiMediaType>;
#endif

namespace Wdc.Rest.Server;

/// <summary>
/// Adds to the document every route mapped by <see cref="RepositoryRpc{TEntity,TCriteria,TKey}"/> and
/// <see cref="RemoteTransactionRoutes"/>, found by the metadata on their endpoints (so the path is the one served,
/// a group's prefix included), with the schemas of their bodies and answers.
/// </summary>
internal sealed class RestOpenApiTransformer : IOpenApiDocumentTransformer
{
    private const string Json = "application/json";
    private const string TransactionsTag = "Remote transactions";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        var endpoints = context.ApplicationServices.GetService<EndpointDataSource>()?.Endpoints ?? [];
        var schemas = new RestOpenApiSchemas(document);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        document.Paths ??= new OpenApiPaths();
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<RestRouteMetadata>() is not { } route
                || endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>() is { ExcludeFromDescription: true })
            {
                continue;
            }

            var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Post;
            var operation = route.Repository is { } repository
                ? RepositoryOperation(document, schemas, route.Route, repository)
                : TransactionOperation(document, schemas, route.Route);
            operation.OperationId = Unique(operationIds, operation.OperationId!);
            var path = PathOf(endpoint.RoutePattern);
            if (!document.Paths.TryGetValue(path, out var item))
            {
                item = new OpenApiPathItem();
                document.Paths[path] = item;
            }

            var pathItem = (OpenApiPathItem)item;
            pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
            pathItem.Operations[HttpMethod.Parse(method)] = operation;
        }

        return Task.CompletedTask;
    }

    // :: Repository routes

    private static OpenApiOperation RepositoryOperation(OpenApiDocument document, RestOpenApiSchemas schemas,
        RestRoute route, RestRepositoryShape repository)
    {
        var model = repository.Model;
        var name = model.Name;
        var query = schemas.Query(model, repository.NewCriteria);
        var operation = route switch
        {
            RestRoute.Count => Operation("Count", $"Counts the {name} rows the query matches.", query,
                Ok("The count.", schemas.Count())),
            RestRoute.Fetch => Operation("Fetch", $"The {name} rows the query matches, at most the server's maximum: "
                + "more than that, without a limit, is refused (page the request).", query,
                Ok("The rows, each with the fields the projection marks and the key.", schemas.Items(model))),
            RestRoute.FetchPage => Operation("FetchPage", $"One page of the {name} rows the query matches, and how many match.",
                query, Ok("The page.", schemas.Page(model))),
            RestRoute.FetchById => Operation("FetchById", $"One {name}, by its key, with the fields the projection marks.",
                schemas.FetchById(model), Ok($"The {name}.", schemas.Entity(model)), NotFound()),
            RestRoute.FetchByRoute => Operation("Get", $"One {name}, by the key in the path, with the default projection.",
                null, Ok($"The {name}.", schemas.Entity(model)), NotFound()),
            RestRoute.Insert => Operation("Insert", $"Inserts a {name}, with the collections it holds.", schemas.Insert(model),
                Ok($"The {name} as stored.", schemas.Inserted(model))),
            RestRoute.Update => Operation("Update", $"Writes the fields present in the body of the {name} its key names: "
                + "an absent field is left alone, a null one cleared, a collection replaced by the list sent.",
                schemas.Entity(model), Ok("Whether something was written.", schemas.Success()), NotFound()),
            _ => Operation("Delete", $"Deletes the {name} rows the query matches; a query that filters nothing is refused.",
                query, Ok("The rows deleted (not counting cascades).", schemas.Count())),
        };

        operation.OperationId = Camel(name) + operation.OperationId;
        operation.Tags = new HashSet<OpenApiTagReference> { Tag(document, name, $"The routes of the {name} repository.") };
        operation.Parameters ??= [];
        if (route == RestRoute.FetchByRoute)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Required = true,
                Description = $"The key of the {name}.",
                Schema = schemas.Key(model),
            });
        }

        operation.Parameters.Add(Header(RestNames.TransactionHeader, false,
            "The remote transaction the request runs in. Without it, a write runs in a transaction of its own."));
        operation.Parameters.Add(Header(RestNames.ClientHeader, false, "The client instance: part of a remote transaction's owner."));
        Errors(operation, schemas, true);
        return operation;
    }

    // :: Transaction routes

    private static OpenApiOperation TransactionOperation(OpenApiDocument document, RestOpenApiSchemas schemas, RestRoute route)
    {
        var operation = route switch
        {
            RestRoute.TransactionBegin => Operation("begin", "Begins a remote transaction for the request's owner.", null,
                Ok("Its id.", schemas.TransactionBegun())),
            RestRoute.TransactionCommit => Operation("commit", "Commits the transaction. Repeating it answers the same; a "
                + "transaction where a request failed is rolled back and refused with 409.", null,
                Ok("Committed.", schemas.TransactionStatus())),
            RestRoute.TransactionRollback => Operation("rollback", "Rolls the transaction back. Repeating it answers the same.",
                null, Ok("Rolled back.", schemas.TransactionStatus())),
            _ => Operation("status", "The outcome of the transaction, for a client that lost the answer to its commit.",
                null, Ok("The state (unknown: never begun, or forgotten).", schemas.TransactionStatus())),
        };

        operation.OperationId = "transaction" + char.ToUpperInvariant(operation.OperationId![0]) + operation.OperationId[1..];
        operation.Tags = new HashSet<OpenApiTagReference>
        {
            Tag(document, TransactionsTag, "A unit of work over several requests, committed or rolled back as one."),
        };
        operation.Parameters ??= [];
        if (route != RestRoute.TransactionBegin)
        {
            operation.Parameters.Add(Header(RestNames.TransactionHeader, true, "The transaction."));
        }

        operation.Parameters.Add(Header(RestNames.ClientHeader, false,
            "The client instance: part of the owner, which every request of the transaction must repeat."));
        Errors(operation, schemas, false);
        return operation;
    }

    // :: Parts

    private static OpenApiOperation Operation(string id, string summary, IOpenApiSchema? body, params (string Status, OpenApiResponse Response)[] answers)
    {
        var operation = new OpenApiOperation
        {
            OperationId = id,
            Summary = summary,
            Responses = [],
        };
        if (body is not null)
        {
            operation.RequestBody = new OpenApiRequestBody { Required = true, Content = Content(body) };
        }

        foreach (var (status, response) in answers)
        {
            operation.Responses[status] = response;
        }

        return operation;
    }

    private static (string, OpenApiResponse) Ok(string description, IOpenApiSchema schema) =>
        ("200", new OpenApiResponse { Description = description, Content = Content(schema) });

    private static (string, OpenApiResponse) NotFound() => ("404", new OpenApiResponse
    {
        Description = "Not found, or out of the caller's reach (the two look the same).",
    });

    private static void Errors(OpenApiOperation operation, RestOpenApiSchemas schemas, bool repository)
    {
        var error = schemas.Error();
        void Add(string status, string description)
        {
            if (!operation.Responses!.ContainsKey(status))
            {
                operation.Responses[status] = new OpenApiResponse { Description = description, Content = Content(error) };
            }
        }

        Add("400", repository ? "A malformed body, a query the server refuses, or a limit passed." : "The X-Tx-Id header is missing.");
        Add("401", "Not authenticated.");
        Add("403", repository ? "Not allowed, or another owner's transaction." : "Another owner's transaction.");
        Add("404", "Not found.");
        Add("409", repository
            ? "The transaction is in use by another request, or a write came without X-Tx-Id while the owner has one open."
            : "The opposite outcome was already reached, or the transaction is in use.");
        Add("410", "The transaction no longer exists.");
        Add("429", "Too many requests or open transactions (Retry-After, when known).");
        Add("500", "Internal server error (the detail is only logged).");
    }

    private static OpenApiParameter Header(string name, bool required, string description) => new()
    {
        Name = name,
        In = ParameterLocation.Header,
        Required = required,
        Description = description,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
    };

    private static MediaTypes Content(IOpenApiSchema schema) => new(StringComparer.Ordinal)
    {
        [Json] = new OpenApiMediaType { Schema = schema },
    };

    private static OpenApiTagReference Tag(OpenApiDocument document, string name, string description)
    {
        document.Tags ??= new HashSet<OpenApiTag>();
        if (!document.Tags.Any(t => t.Name == name))
        {
            document.Tags.Add(new OpenApiTag { Name = name, Description = description });
        }

        return new OpenApiTagReference(name, document);
    }

    // The template of the route as OpenAPI writes it: parameters by name, without constraints or defaults.
    private static string PathOf(RoutePattern pattern)
    {
        var path = new System.Text.StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            path.Append('/');
            foreach (var part in segment.Parts)
            {
                path.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    RoutePatternParameterPart parameter => "{" + parameter.Name + "}",
                    _ => string.Empty,
                });
            }
        }

        return path.Length == 0 ? "/" : path.ToString();
    }

    private static string Unique(HashSet<string> used, string id)
    {
        var candidate = id;
        for (var n = 2; !used.Add(candidate); n++)
        {
            candidate = id + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
