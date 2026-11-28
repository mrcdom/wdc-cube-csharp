using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>What a route of this package does, on its endpoint: read by the OpenAPI description.</summary>
internal sealed class RestRouteMetadata(RestRoute route, RestRepositoryShape? repository = null)
{
    public RestRoute Route { get; } = route;

    /// <summary>The repository behind a repository route; <see langword="null"/> for a transaction route.</summary>
    public RestRepositoryShape? Repository { get; } = repository;
}

/// <summary>The model and criteria a repository's routes carry.</summary>
internal sealed class RestRepositoryShape(IEntityModel model, Func<ICriteria> newCriteria)
{
    public IEntityModel Model { get; } = model;

    public Func<ICriteria> NewCriteria { get; } = newCriteria;
}

internal enum RestRoute
{
    Count,
    Fetch,
    FetchPage,
    FetchById,
    FetchByRoute,
    Insert,
    Update,
    Delete,
    TransactionBegin,
    TransactionCommit,
    TransactionRollback,
    TransactionStatus,
}
