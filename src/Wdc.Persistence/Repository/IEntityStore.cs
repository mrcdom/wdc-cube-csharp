namespace Wdc.Persistence;

/// <summary>
/// What a cascade needs from the repository of a child (decision D-23): the child is written by its own repository,
/// so its rules (a validation in an overridden <c>Insert</c>) hold in cascade too.
/// </summary>
internal interface IEntityStore<TEntity>
    where TEntity : class
{
    Task<bool> InsertRow(TEntity bean, TEntity? projection, CancellationToken cancellationToken);

    Task<bool> UpdateRow(TEntity bean, TEntity projection, CancellationToken cancellationToken);

    /// <summary>Deletes the rows with these keys (model form), their declared cascade children first.</summary>
    Task DeleteByKeys(IReadOnlyList<object> keys, CancellationToken cancellationToken);

    /// <summary>The keys (model form) of the rows whose <paramref name="column"/> is one of <paramref name="values"/> (database form).</summary>
    Task<IReadOnlyList<object>> KeysWhere(string column, IReadOnlyList<object> values, CancellationToken cancellationToken);
}
