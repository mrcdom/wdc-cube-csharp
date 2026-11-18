namespace Wdc.Domain;

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
