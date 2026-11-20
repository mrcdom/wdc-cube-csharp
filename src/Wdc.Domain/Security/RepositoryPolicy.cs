namespace Wdc.Domain;

/// <summary>
/// The rules of an entity per role: what each actor may do, which rows they reach, which fields they
/// read, and the checks of each write. Applied by <see cref="ProtectedRepository{TEntity,TCriteria,TKey}"/>, in any host,
/// and to the entity written in cascade through another one.
/// </summary>
/// <remarks>
/// <para>Order of a read: <see cref="Authorize"/> → <see cref="SanitizeProjection"/> → <see cref="Scope"/> → repository
/// → <see cref="SanitizeOutput"/>. Insert: authorize → <see cref="BeforeInsert"/> → insert → sanitize the output.
/// Update: authorize → the row must be within <see cref="Scope"/> → <see cref="BeforeUpdate"/> → update. Delete:
/// authorize → scope → <see cref="BeforeDelete"/> → delete. The checks of a write run inside its transaction.</para>
/// <para><see cref="Scope"/> must narrow with <c>Restrict</c> (<c>c.UserId.Restrict(owner)</c>): a restriction is
/// combined with AND outside whatever was asked on the same field, so <c>"or": true</c> cannot widen it.</para>
/// <para>The policy holds no actor: one instance serves every session.</para>
/// </remarks>
public sealed class RepositoryPolicy<TEntity, TCriteria, TKey>
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    public RepositoryPolicy(EntityModel<TEntity> model, Func<TCriteria> newCriteria)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(newCriteria);
        Model = model;
        NewCriteria = newCriteria;
    }

    public EntityModel<TEntity> Model { get; }

    public Func<TCriteria> NewCriteria { get; }

    /// <summary>Throws <see cref="AccessDeniedException"/> unless the actor may do the operation.</summary>
    public Action<PolicyContext, RepositoryOperation>? Authorize { get; set; }

    /// <summary>Narrows a criteria to the actor's reach, with <c>Restrict</c>. Applies to reads, update and delete.</summary>
    public Action<PolicyContext, TCriteria>? Scope { get; set; }

    /// <summary>
    /// Restricts a criteria to one key: needed for <see cref="Scope"/> to guard update, fetch by id and cascades, and for
    /// <see cref="BeforeDelete"/> to check a child deleted in cascade.
    /// </summary>
    public Action<TCriteria, TKey>? RestrictToId { get; set; }

    /// <summary>Removes from a requested projection what the actor may not read (a password).</summary>
    public Action<PolicyContext, TEntity>? SanitizeProjection { get; set; }

    /// <summary>Removes from an answer what the actor may not see.</summary>
    public Action<PolicyContext, TEntity>? SanitizeOutput { get; set; }

    /// <summary>Checks or completes an entity before it is inserted.</summary>
    public Func<PolicyContext, TEntity, CancellationToken, ValueTask>? BeforeInsert { get; set; }

    /// <summary>Checks or completes an update before it is written; the third argument marks the fields written.</summary>
    public Func<PolicyContext, TEntity, TEntity, CancellationToken, ValueTask>? BeforeUpdate { get; set; }

    /// <summary>Checks a delete, given the criteria of the rows that would go (already within the actor's reach).</summary>
    public Func<PolicyContext, TCriteria, CancellationToken, ValueTask>? BeforeDelete { get; set; }

    /// <summary>A criteria for one key, within the actor's reach.</summary>
    internal TCriteria KeyCriteria(PolicyContext context, TKey key)
    {
        var restrict = RestrictToId
            ?? throw new InvalidOperationException($"{Model.Name}: a policy with a Scope or a BeforeDelete needs RestrictToId.");
        var criteria = NewCriteria();
        restrict(criteria, key);
        Scope?.Invoke(context, criteria);
        return criteria;
    }
}
