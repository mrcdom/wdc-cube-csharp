namespace Wdc.Domain;

/// <summary>
/// Which rows of an entity a <see cref="DataGrant{TEntity, TCriteria}"/> reaches: all of them, or those related to the
/// actor (their own user, their purchases).
/// </summary>
/// <remarks>
/// <para>It is said twice, in two languages, and both must agree: <see cref="Restrict"/> as criteria, so the database
/// returns only the reachable rows; <see cref="Covers"/> in memory, so each returned row gets the fields of the grants
/// that reach it. An actor with two roles may reach a row through one and not the other, and the fields of that row
/// are those of the grants that reach it, not everything the actor may read anywhere. On a write, <see cref="Covers"/>
/// runs on the row being created, and on the stored row loaded for an update.</para>
/// <para>A policy compares scopes by identity: declare each one once and share it among the grants.</para>
/// </remarks>
public interface IRowScope<TEntity, in TCriteria>
    where TEntity : class
{
    /// <summary>Sets on <paramref name="group"/>, an empty criteria, what selects the reachable rows.</summary>
    void Restrict(TCriteria group, Actor actor);

    /// <summary>
    /// Whether <paramref name="row"/> is reachable. It reads only what <see cref="Projection"/> names, which is fetched
    /// for it; it may look up what the row does not carry (the owner of the purchase an item points to), inside the
    /// write's transaction on a write.
    /// </summary>
    ValueTask<bool> Covers(TEntity row, Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// What <see cref="Covers"/> reads beyond the key, possibly inside references; <see langword="null"/> when it reads
    /// only the key. A new projection on each call.
    /// </summary>
    TEntity? Projection() => null;
}
