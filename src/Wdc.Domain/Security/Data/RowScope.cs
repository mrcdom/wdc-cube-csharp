namespace Wdc.Domain;

/// <summary>The row scopes every policy knows.</summary>
public static class RowScope
{
    /// <summary>Every row: nothing to restrict, every row covered.</summary>
    public static IRowScope<TEntity, TCriteria> All<TEntity, TCriteria>()
        where TEntity : class => AllRows<TEntity, TCriteria>.Instance;
}

/// <summary><see cref="RowScope.All{TEntity, TCriteria}"/>.</summary>
internal sealed class AllRows<TEntity, TCriteria> : IRowScope<TEntity, TCriteria>
    where TEntity : class
{
    public static readonly AllRows<TEntity, TCriteria> Instance = new();

    private AllRows()
    {
    }

    public void Restrict(TCriteria group, Actor actor)
    {
    }

    public ValueTask<bool> Covers(TEntity row, Actor actor, CancellationToken cancellationToken) => ValueTask.FromResult(true);

    public override string ToString() => "ALL";
}
