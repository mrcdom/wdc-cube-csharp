namespace Wdc.Domain;

/// <summary>
/// Shapes the rows a read returned, per row, by the grants that reach it. Obtained from
/// <see cref="DataAccess{TEntity, TCriteria}.PrepareRead"/>; apply it before the rows leave.
/// </summary>
public sealed class ReadPlan<TEntity>
    where TEntity : class
{
    private readonly Func<TEntity, CancellationToken, ValueTask<TEntity>> _apply;

    internal ReadPlan(Func<TEntity, CancellationToken, ValueTask<TEntity>> apply) => _apply = apply;

    /// <summary>Shapes <paramref name="row"/> in place, and returns it.</summary>
    public ValueTask<TEntity> Apply(TEntity row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _apply(row, cancellationToken);
    }

    /// <summary>Shapes every row in place, and returns them.</summary>
    public async ValueTask<IReadOnlyList<TEntity>> ApplyAll(IReadOnlyList<TEntity> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in rows)
        {
            await _apply(row, cancellationToken);
        }

        return rows;
    }
}
