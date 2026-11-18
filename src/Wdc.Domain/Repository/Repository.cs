namespace Wdc.Domain;

/// <summary>Implemented by every entity: its key, for references, graphs and fetching by id.</summary>
public interface IKeyedEntity
{
    object? Key { get; }
}

/// <summary>One page of a listing.</summary>
/// <param name="Items">The items of this page.</param>
/// <param name="Number">The page, zero-based.</param>
/// <param name="TotalPages">How many pages exist.</param>
/// <param name="TotalItems">How many items exist.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int Number, int TotalPages, int TotalItems)
{
    public static Page<T> Of(IReadOnlyList<T> items, int page, int pageSize, int totalItems)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var totalPages = totalItems == 0 ? 0 : (totalItems + pageSize - 1) / pageSize;
        return new Page<T>(items, page, totalPages, totalItems);
    }
}

/// <summary>
/// Queries an entity. Who only reads depends on this, and says so by its type; it also serves data that is never
/// written through here (a dashboard view, a report).
/// </summary>
public interface IReadOnlyRepository<TEntity, in TCriteria, in TKey>
    where TEntity : class
    where TCriteria : ICriteria
{
    Task<int> Count(TCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// The entities that match, shaped by the criteria's projection (or <see cref="OwnFieldsProjection"/>).
    /// <c>0</c> in <paramref name="offset"/> or <paramref name="limit"/> means "no skip" and "no limit".
    /// </summary>
    Task<IReadOnlyList<TEntity>> Fetch(TCriteria criteria, int offset = 0, int limit = 0,
        CancellationToken cancellationToken = default);

    /// <summary>One page (zero-based) and the totals.</summary>
    Task<Page<TEntity>> FetchPage(TCriteria criteria, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The entity with this key, through the same rules as <see cref="Fetch"/>; or <see langword="null"/>.</summary>
    Task<TEntity?> FetchById(TKey id, TEntity? projection = null, CancellationToken cancellationToken = default);

    /// <summary>The default projection: own scalar fields and the key of each reference.</summary>
    TEntity OwnFieldsProjection();
}

/// <summary>
/// Queries and writes an entity. Implemented on the server over the database and on the client over HTTP: whoever
/// writes a presenter or a service does not know which side it is on.
/// </summary>
/// <remarks>
/// Repositories never open or close transactions; they join the one that is open. A write called with no
/// transaction open runs in one of its own (REQUIRED).
/// </remarks>
public interface IRepository<TEntity, in TCriteria, in TKey> : IReadOnlyRepository<TEntity, TCriteria, TKey>
    where TEntity : class
    where TCriteria : ICriteria
{
    /// <summary>
    /// Inserts the fields <paramref name="projection"/> marks (without one, the fields that hold a value; the rest
    /// get the column default). The bean comes back completed: the generated key and what the database filled.
    /// </summary>
    Task<bool> Insert(TEntity bean, TEntity? projection = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the fields <paramref name="projection"/> marks; with <paramref name="oldBean"/>, only the ones that
    /// changed. A marked field holding null clears the column. Returns false when nothing was written.
    /// </summary>
    Task<bool> Update(TEntity newBean, TEntity? oldBean = null, TEntity? projection = null,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts when <paramref name="oldBean"/> is null, updates otherwise; never asks the database to decide.</summary>
    Task<bool> InsertOrUpdate(TEntity newBean, TEntity? oldBean, TEntity? projection = null,
        CancellationToken cancellationToken = default) =>
        oldBean is null
            ? Insert(newBean, projection, cancellationToken)
            : Update(newBean, oldBean, projection, cancellationToken);

    /// <summary>Deletes what matches. A criteria with no filter is refused: deleting everything is not a use case.</summary>
    /// <exception cref="InvalidRequestException">The criteria has no filter.</exception>
    Task<int> Delete(TCriteria criteria, CancellationToken cancellationToken = default);
}
