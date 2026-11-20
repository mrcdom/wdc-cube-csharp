using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// Serves a repository as the routes of the Kotlin version: <c>POST {base}/insert</c>, <c>update</c>,
/// <c>delete</c>, <c>count</c>, <c>fetch</c>, <c>fetch-page</c>, <c>fetch-by-id</c> and <c>GET {base}/{id}</c>.
/// </summary>
/// <remarks>
/// Only the wire: who may do what is the repository's business. The function that gives the repository of a request
/// hands out a <see cref="ProtectedRepository{TEntity,TCriteria,TKey}"/> for whoever the request authenticated, so
/// the rules are the same here and in any other host.
/// </remarks>
public sealed class RepositoryRpc<TEntity, TCriteria, TKey>
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    private readonly Func<HttpContext, IReadOnlyRepository<TEntity, TCriteria, TKey>> _repository;

    /// <param name="basePath">Where the routes live (<c>/api/repo/product</c>).</param>
    /// <param name="model">The entity model (the same the client uses).</param>
    /// <param name="newCriteria">A fresh criteria.</param>
    /// <param name="repository">The repository for the request (resolved per request).</param>
    public RepositoryRpc(string basePath, EntityModel<TEntity> model, Func<TCriteria> newCriteria,
        Func<HttpContext, IReadOnlyRepository<TEntity, TCriteria, TKey>> repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentNullException.ThrowIfNull(repository);
        BasePath = basePath;
        Wire = new RepositoryWire<TEntity, TCriteria>(model, newCriteria);
        _repository = repository;
    }

    public string BasePath { get; }

    public RepositoryWire<TEntity, TCriteria> Wire { get; }

    /// <summary>Whether the write routes are served (the repository must then be an <see cref="IRepository{TEntity,TCriteria,TKey}"/>).</summary>
    public bool Writes { get; set; } = true;

    public RouteGroupBuilder Map(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var group = routes.MapGroup(BasePath);
        group.MapPost(RestNames.Count, c => Handle(c, false, Count));
        group.MapPost(RestNames.Fetch, c => Handle(c, false, Fetch));
        group.MapPost(RestNames.FetchPage, c => Handle(c, false, FetchPage));
        group.MapPost(RestNames.FetchById, c => Handle(c, false, FetchById));
        group.MapGet("{id}", c => Handle(c, false, FetchByRoute));
        if (Writes)
        {
            group.MapPost(RestNames.Insert, c => Handle(c, true, Insert));
            group.MapPost(RestNames.Update, c => Handle(c, true, Update));
            group.MapPost(RestNames.Delete, c => Handle(c, true, Delete));
        }

        return group;
    }

    // :: Pipeline

    private static Task Handle(HttpContext context, bool write, Func<HttpContext, byte[], Task<byte[]?>> handler) =>
        RestHttp.Respond(context, async () =>
        {
            var body = HttpMethods.IsGet(context.Request.Method) ? [] : await RestHttp.ReadBody(context);
            return await RestTransactions.Run(context, write, () => handler(context, body));
        });

    // :: Reads

    private async Task<byte[]?> Count(HttpContext context, byte[] body)
    {
        var (criteria, _) = ReadQuery(context, body);
        return RepositoryWire<TEntity, TCriteria>.WriteCount(await Repository(context).Count(criteria, context.RequestAborted));
    }

    private async Task<byte[]?> Fetch(HttpContext context, byte[] body)
    {
        var (criteria, slice) = ReadQuery(context, body);
        var max = RestHttp.Options(context).MaxItems;
        if (slice.Limit > max)
        {
            throw new InvalidRequestException($"'limit' cannot be larger than {max}.");
        }

        var limit = slice.Limit == 0 ? max + 1 : slice.Limit;
        var items = await Repository(context).Fetch(criteria, slice.Offset, limit, context.RequestAborted);
        if (items.Count > max)
        {
            throw new InvalidRequestException($"More than {max} items match: page the request (offset/limit).");
        }

        return Wire.WriteItems(items, criteria.Projection);
    }

    private async Task<byte[]?> FetchPage(HttpContext context, byte[] body)
    {
        var (criteria, slice) = ReadQuery(context, body);
        var max = RestHttp.Options(context).MaxItems;
        if (slice.PageSize is <= 0 || slice.PageSize > max)
        {
            throw new InvalidRequestException($"'pageSize' must be between 1 and {max}.");
        }

        var page = await Repository(context).FetchPage(criteria, slice.Page, slice.PageSize, context.RequestAborted);
        return Wire.WriteItems(page.Items, criteria.Projection, page.TotalItems);
    }

    private Task<byte[]?> FetchById(HttpContext context, byte[] body)
    {
        var (holder, projection) = Wire.ReadFetchById(body);
        return FetchOne(context, KeyOf(holder), projection);
    }

    private Task<byte[]?> FetchByRoute(HttpContext context, byte[] body)
    {
        var text = context.Request.RouteValues["id"] as string ?? throw new InvalidRequestException("'id' is required");
        return FetchOne(context, KeyOf(Wire.KeyFromRoute(text)), null);
    }

    private async Task<byte[]?> FetchOne(HttpContext context, TKey key, TEntity? projection)
    {
        var repository = Repository(context);
        projection ??= repository.OwnFieldsProjection();
        var found = await repository.FetchById(key, projection, context.RequestAborted);
        return found is null ? null : Wire.WriteEntity(found, projection); // null: 404
    }

    // :: Writes

    private async Task<byte[]?> Insert(HttpContext context, byte[] body)
    {
        var (entity, projection) = Wire.ReadInsert(body);
        var success = await Writable(context).Insert(entity, projection, context.RequestAborted);
        return Wire.WriteInsertResult(success, entity);
    }

    private async Task<byte[]?> Update(HttpContext context, byte[] body)
    {
        var (entity, presence) = Wire.ReadUpdate(body);
        if (Wire.Model.KeyOf(entity) is null)
        {
            throw new InvalidRequestException($"update of {Wire.Model.Name} without its key");
        }

        return RepositoryWire<TEntity, TCriteria>.WriteSuccess(
            await Writable(context).Update(entity, null, presence, context.RequestAborted));
    }

    private async Task<byte[]?> Delete(HttpContext context, byte[] body)
    {
        var (criteria, _) = Wire.ReadQuery(body);
        if (!criteria.HasFilter)
        {
            throw new InvalidRequestException($"delete of {Wire.Model.Name} without a filter");
        }

        return RepositoryWire<TEntity, TCriteria>.WriteCount(await Writable(context).Delete(criteria, context.RequestAborted));
    }

    // :: Internals

    private (TCriteria Criteria, Slice Slice) ReadQuery(HttpContext context, byte[] body)
    {
        var (criteria, slice) = Wire.ReadQuery(body);
        criteria.Projection ??= Repository(context).OwnFieldsProjection();
        return (criteria, slice);
    }

    private TKey KeyOf(TEntity holder) => (TKey)Wire.Model.KeyOf(holder)!;

    private IReadOnlyRepository<TEntity, TCriteria, TKey> Repository(HttpContext context) => _repository(context);

    private IRepository<TEntity, TCriteria, TKey> Writable(HttpContext context) =>
        _repository(context) as IRepository<TEntity, TCriteria, TKey>
        ?? throw new InvalidOperationException($"{BasePath}: the repository does not write.");
}
