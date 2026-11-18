using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// Serves a repository as the routes of the Kotlin version (decision D-18): <c>POST {base}/insert</c>, <c>update</c>,
/// <c>delete</c>, <c>count</c>, <c>fetch</c>, <c>fetch-page</c>, <c>fetch-by-id</c> and <c>GET {base}/{id}</c>.
/// </summary>
/// <remarks>
/// <para>Order of a read: authorize → read the body → <see cref="SanitizeProjection"/> → <see cref="Scope"/> →
/// repository → <see cref="SanitizeOutput"/>. Insert: authorize → read → <see cref="BeforeInsert"/> → insert →
/// <see cref="SanitizeOutput"/>. Update: authorize → read → the row must be within <see cref="Scope"/> →
/// <see cref="BeforeUpdate"/> → update.</para>
/// <para><see cref="Scope"/> must narrow with <c>Restrict</c> (<c>c.UserId.Restrict(owner)</c>): a restriction is
/// combined with AND outside whatever the client asked on the same field, so <c>"or": true</c> cannot widen it.</para>
/// </remarks>
public sealed class RepositoryRpc<TEntity, TCriteria, TKey> : ICascadeRules
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

    /// <summary>Checks the caller may do the operation; throws <see cref="AuthenticationException"/> or <see cref="AccessDeniedException"/>.</summary>
    public Func<HttpContext, RpcOperation, ValueTask>? Authorize { get; set; }

    /// <summary>Removes from a requested projection what the caller may not read (a password).</summary>
    public Action<HttpContext, TEntity>? SanitizeProjection { get; set; }

    /// <summary>Narrows a criteria to the caller's reach, with <c>Restrict</c>. Applies to reads, update and delete.</summary>
    public Action<HttpContext, TCriteria>? Scope { get; set; }

    /// <summary>Restricts a criteria to one key: needed for <see cref="Scope"/> to guard update and fetch by id.</summary>
    public Action<TCriteria, TKey>? RestrictToId { get; set; }

    /// <summary>Checks or completes an entity before it is inserted (inside the transaction).</summary>
    public Func<HttpContext, TEntity, ValueTask>? BeforeInsert { get; set; }

    /// <summary>Checks or completes an update before it is written (inside the transaction); the second argument marks the fields sent.</summary>
    public Func<HttpContext, TEntity, TEntity, ValueTask>? BeforeUpdate { get; set; }

    /// <summary>Removes from an answer what the caller may not see.</summary>
    public Action<HttpContext, TEntity>? SanitizeOutput { get; set; }

    public RouteGroupBuilder Map(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        (routes.ServiceProvider.GetService(typeof(RpcRegistry)) as RpcRegistry)?.Add(typeof(TEntity), this);
        var group = routes.MapGroup(BasePath);
        group.MapPost(RestNames.Count, c => Handle(c, RpcOperation.Count, false, Count));
        group.MapPost(RestNames.Fetch, c => Handle(c, RpcOperation.Fetch, false, Fetch));
        group.MapPost(RestNames.FetchPage, c => Handle(c, RpcOperation.FetchPage, false, FetchPage));
        group.MapPost(RestNames.FetchById, c => Handle(c, RpcOperation.FetchById, false, FetchById));
        group.MapGet("{id}", c => Handle(c, RpcOperation.FetchById, false, FetchByRoute));
        if (Writes)
        {
            group.MapPost(RestNames.Insert, c => Handle(c, RpcOperation.Insert, true, Insert));
            group.MapPost(RestNames.Update, c => Handle(c, RpcOperation.Update, true, Update));
            group.MapPost(RestNames.Delete, c => Handle(c, RpcOperation.Delete, true, Delete));
        }

        return group;
    }

    // :: Pipeline

    private Task Handle(HttpContext context, RpcOperation operation, bool write, Func<HttpContext, byte[], Task<byte[]?>> handler) =>
        RestHttp.Respond(context, async () =>
        {
            if (Authorize is { } authorize)
            {
                await authorize(context, operation);
            }

            var body = HttpMethods.IsGet(context.Request.Method) ? [] : await RestHttp.ReadBody(context);
            if (!write)
            {
                return await RestTransactions.Run(context, write, () => handler(context, body));
            }

            // Children written in cascade answer to their own routes' rules (decision D-23). Without the registry
            // (AddCubeRest not called) the guard knows no routes and refuses every cascade.
            var registry = context.RequestServices.GetService(typeof(RpcRegistry)) as RpcRegistry ?? new RpcRegistry();
            var guard = new RpcCascadeGuard(context, registry);
            return await RestTransactions.Run(context, write, () => CascadeGuard.Run(guard, () => handler(context, body)));
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

        Output(context, items);
        return Wire.WriteItems(items, ProjectionOf(context, criteria));
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
        Output(context, page.Items);
        return Wire.WriteItems(page.Items, ProjectionOf(context, criteria), page.TotalItems);
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
        projection ??= Repository(context).OwnFieldsProjection();
        SanitizeProjection?.Invoke(context, projection);
        TEntity? found;
        if (Scope is { } scope)
        {
            // Through the criteria, so the reach applies.
            var criteria = KeyCriteria(key);
            criteria.Projection = projection;
            scope(context, criteria);
            var rows = await Repository(context).Fetch(criteria, 0, 1, context.RequestAborted);
            found = rows.Count == 0 ? null : rows[0];
        }
        else
        {
            found = await Repository(context).FetchById(key, projection, context.RequestAborted);
        }

        if (found is null)
        {
            return null; // 404
        }

        SanitizeOutput?.Invoke(context, found);
        return Wire.WriteEntity(found, projection);
    }

    // :: Writes

    private async Task<byte[]?> Insert(HttpContext context, byte[] body)
    {
        var (entity, projection) = Wire.ReadInsert(body);
        if (BeforeInsert is { } before)
        {
            await before(context, entity);
        }

        var success = await Writable(context).Insert(entity, projection, context.RequestAborted);
        if (success)
        {
            SanitizeOutput?.Invoke(context, entity);
        }

        return Wire.WriteInsertResult(success, entity);
    }

    private async Task<byte[]?> Update(HttpContext context, byte[] body)
    {
        var (entity, presence) = Wire.ReadUpdate(body);
        if (Wire.Model.KeyOf(entity) is not { } key)
        {
            throw new InvalidRequestException($"update of {Wire.Model.Name} without its key");
        }

        await RequireWithinReach(context, (TKey)key);

        if (BeforeUpdate is { } before)
        {
            await before(context, entity, presence);
        }

        return RepositoryWire<TEntity, TCriteria>.WriteSuccess(
            await Writable(context).Update(entity, null, presence, context.RequestAborted));
    }

    private async Task<byte[]?> Delete(HttpContext context, byte[] body)
    {
        var (criteria, _) = Wire.ReadQuery(body);
        if (!criteria.HasFilter)
        {
            // Checked before the reach is added: "delete everything I can reach" is not a use case either.
            throw new InvalidRequestException($"delete of {Wire.Model.Name} without a filter");
        }

        Scope?.Invoke(context, criteria);
        return RepositoryWire<TEntity, TCriteria>.WriteCount(await Writable(context).Delete(criteria, context.RequestAborted));
    }

    // :: Rules for a child written in cascade (decision D-23)

    async Task ICascadeRules.CheckInsert(HttpContext context, object entity)
    {
        if (Authorize is { } authorize)
        {
            await authorize(context, RpcOperation.Insert);
        }

        if (BeforeInsert is { } before)
        {
            await before(context, (TEntity)entity);
        }
    }

    async Task ICascadeRules.CheckUpdate(HttpContext context, object entity, object projection)
    {
        if (Authorize is { } authorize)
        {
            await authorize(context, RpcOperation.Update);
        }

        if (Wire.Model.KeyOf((TEntity)entity) is { } key)
        {
            await RequireWithinReach(context, (TKey)key);
        }

        if (BeforeUpdate is { } before)
        {
            await before(context, (TEntity)entity, (TEntity)projection);
        }
    }

    async Task ICascadeRules.CheckDelete(HttpContext context, IReadOnlyList<object> keys)
    {
        if (Authorize is { } authorize)
        {
            await authorize(context, RpcOperation.Delete);
        }

        foreach (var key in keys)
        {
            await RequireWithinReach(context, (TKey)key);
        }
    }

    private async Task RequireWithinReach(HttpContext context, TKey key)
    {
        if (Scope is not { } scope)
        {
            return;
        }

        var criteria = KeyCriteria(key);
        scope(context, criteria);
        if (await Repository(context).Count(criteria, context.RequestAborted) == 0)
        {
            throw new KeyNotFoundException($"No {Wire.Model.Name} {key} within reach.");
        }
    }

    // :: Internals

    private (TCriteria Criteria, Slice Slice) ReadQuery(HttpContext context, byte[] body)
    {
        var (criteria, slice) = Wire.ReadQuery(body);
        criteria.Projection ??= Repository(context).OwnFieldsProjection();
        SanitizeProjection?.Invoke(context, criteria.Projection);
        Scope?.Invoke(context, criteria);
        return (criteria, slice);
    }

    private static TEntity? ProjectionOf(HttpContext context, TCriteria criteria) => criteria.Projection;

    private void Output(HttpContext context, IReadOnlyList<TEntity> items)
    {
        if (SanitizeOutput is { } sanitize)
        {
            foreach (var item in items)
            {
                sanitize(context, item);
            }
        }
    }

    private TCriteria KeyCriteria(TKey key)
    {
        var restrict = RestrictToId
            ?? throw new InvalidOperationException($"{BasePath}: a Scope needs RestrictToId to guard update and fetch by id.");
        var criteria = Wire.NewCriteria();
        restrict(criteria, key);
        return criteria;
    }

    private TKey KeyOf(TEntity holder) => (TKey)Wire.Model.KeyOf(holder)!;

    private IReadOnlyRepository<TEntity, TCriteria, TKey> Repository(HttpContext context) => _repository(context);

    private IRepository<TEntity, TCriteria, TKey> Writable(HttpContext context) =>
        _repository(context) as IRepository<TEntity, TCriteria, TKey>
        ?? throw new InvalidOperationException($"{BasePath}: the repository does not write.");
}
