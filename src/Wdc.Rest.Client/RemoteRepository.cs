using Wdc.Domain;

namespace Wdc.Rest.Client;

/// <summary>
/// A repository on the other side of HTTP, with the contract of the local one: the presenter or service that uses it
/// does not know which side it is on. Writes outside a remote transaction run in one of their own on the server
/// (implicit REQUIRED, as locally).
/// </summary>
public class RemoteRepository<TEntity, TCriteria, TKey> : IRepository<TEntity, TCriteria, TKey>
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    /// <param name="client">The transport.</param>
    /// <param name="basePath">Where the routes live (<c>/api/repo/product</c>).</param>
    /// <param name="model">The entity model (the same the server uses).</param>
    /// <param name="newCriteria">A fresh criteria.</param>
    public RemoteRepository(RestClient client, string basePath, EntityModel<TEntity> model, Func<TCriteria> newCriteria)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        Client = client;
        BasePath = basePath.TrimEnd('/');
        Wire = new RepositoryWire<TEntity, TCriteria>(model, newCriteria);
    }

    protected RestClient Client { get; }

    protected string BasePath { get; }

    protected RepositoryWire<TEntity, TCriteria> Wire { get; }

    protected EntityModel<TEntity> Model => Wire.Model;

    public virtual TEntity OwnFieldsProjection() => Model.OwnFieldsProjection();

    // :: Reads

    public virtual async Task<int> Count(TCriteria criteria, CancellationToken cancellationToken = default) =>
        RepositoryWire<TEntity, TCriteria>.ReadCount(
            await Client.Post(Route(RestNames.Count), Wire.WriteQuery(criteria), cancellationToken).ConfigureAwait(false));

    public virtual async Task<IReadOnlyList<TEntity>> Fetch(TCriteria criteria, int offset = 0, int limit = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        var body = await Client.Post(Route(RestNames.Fetch), Wire.WriteQuery(criteria, new Slice(offset, limit)), cancellationToken)
            .ConfigureAwait(false);
        return Wire.ReadItems(body).Items;
    }

    public virtual async Task<Page<TEntity>> FetchPage(TCriteria criteria, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var body = await Client.Post(Route(RestNames.FetchPage), Wire.WriteQuery(criteria, new Slice(Page: page, PageSize: pageSize)),
            cancellationToken).ConfigureAwait(false);
        var (items, total) = Wire.ReadItems(body);
        return Page<TEntity>.Of(items, page, pageSize, total);
    }

    public virtual async Task<TEntity?> FetchById(TKey id, TEntity? projection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        var body = await Client.PostOrNull(Route(RestNames.FetchById), Wire.WriteFetchById(id, projection), cancellationToken)
            .ConfigureAwait(false);
        return body is null ? null : Wire.ReadEntity(body);
    }

    // :: Writes

    public virtual async Task<bool> Insert(TEntity bean, TEntity? projection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        var body = await Client.Post(Route(RestNames.Insert), Wire.WriteInsert(bean, projection), cancellationToken)
            .ConfigureAwait(false);
        var (success, stored) = Wire.ReadInsertResult(body);
        if (!success || stored is null)
        {
            return false;
        }

        // As the local repository: what was not written comes back as stored (the key, defaults, triggers).
        foreach (var field in Model.Fields)
        {
            if (field.Kind == FieldKind.Collection)
            {
                continue;
            }

            var written = !field.IsKey && (projection is null ? field.HasValue(bean) : field.HasValue(projection));
            if (!written)
            {
                field.Copy(stored, bean);
            }
        }

        return true;
    }

    public virtual async Task<bool> Update(TEntity newBean, TEntity? oldBean = null, TEntity? projection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newBean);
        if (Model.KeyOf(newBean) is null)
        {
            throw new InvalidRequestException($"update of {Model.Name} without its key");
        }

        // The same choice of fields as the local repository; the server writes exactly the ones sent.
        var marks = projection ?? (oldBean is null ? OwnFieldsProjection() : Model.ChangedFields(newBean, oldBean));
        var fields = Model.New();
        var any = false;
        foreach (var field in Model.Fields)
        {
            if (field.IsKey || field.Kind == FieldKind.Collection || !field.HasValue(marks))
            {
                continue;
            }

            if (oldBean is not null && projection is not null && !field.Differs(newBean, oldBean))
            {
                continue; // marked but unchanged
            }

            field.Copy(marks, fields);
            any = true;
        }

        if (!any)
        {
            return false;
        }

        var body = await Client.Post(Route(RestNames.Update), Wire.WriteUpdate(newBean, fields), cancellationToken).ConfigureAwait(false);
        return RepositoryWire<TEntity, TCriteria>.ReadSuccess(body);
    }

    public virtual async Task<int> Delete(TCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (!criteria.HasFilter)
        {
            throw new InvalidRequestException($"delete of {Model.Name} without a filter");
        }

        return RepositoryWire<TEntity, TCriteria>.ReadCount(
            await Client.Post(Route(RestNames.Delete), Wire.WriteQuery(criteria), cancellationToken).ConfigureAwait(false));
    }

    protected string Route(string operation) => $"{BasePath}/{operation}";
}
