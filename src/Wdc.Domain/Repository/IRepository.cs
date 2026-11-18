namespace Wdc.Domain;

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
