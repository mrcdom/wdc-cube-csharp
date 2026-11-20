namespace Wdc.Domain;

/// <summary>
/// A repository seen by whoever is acting: every operation goes through the entity's
/// <see cref="RepositoryPolicy{TEntity,TCriteria,TKey}"/>, and so does every child it writes in cascade. This is what
/// a host hands to a session — in Blazor Server as in the REST server — so the rules hold in every host.
/// </summary>
/// <remarks>
/// The scope and the sanitizing change the criteria and the projection the caller passed (as the REST server always
/// did with what it read from the wire). Subclass it to protect what an application repository adds (an image).
/// </remarks>
public class ProtectedRepository<TEntity, TCriteria, TKey> : IRepository<TEntity, TCriteria, TKey>, ICascadePolicy
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    public ProtectedRepository(IRepository<TEntity, TCriteria, TKey> inner, RepositoryPolicy<TEntity, TCriteria, TKey> policy,
        Protection protection)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(protection);
        Inner = inner;
        Policy = policy;
        Protection = protection;
        protection.Add(typeof(TEntity), this);
    }

    protected IRepository<TEntity, TCriteria, TKey> Inner { get; }

    protected RepositoryPolicy<TEntity, TCriteria, TKey> Policy { get; }

    protected Protection Protection { get; }

    public TEntity OwnFieldsProjection() => Inner.OwnFieldsProjection();

    // :: Reads

    public Task<int> Count(TCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var context = Enter(RepositoryOperation.Count);
        Policy.Scope?.Invoke(context, criteria);
        return Inner.Count(criteria, cancellationToken);
    }

    public async Task<IReadOnlyList<TEntity>> Fetch(TCriteria criteria, int offset = 0, int limit = 0,
        CancellationToken cancellationToken = default)
    {
        var context = Narrow(RepositoryOperation.Fetch, criteria);
        return Output(context, await Inner.Fetch(criteria, offset, limit, cancellationToken));
    }

    public async Task<Page<TEntity>> FetchPage(TCriteria criteria, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var context = Narrow(RepositoryOperation.FetchPage, criteria);
        var found = await Inner.FetchPage(criteria, page, pageSize, cancellationToken);
        Output(context, found.Items);
        return found;
    }

    public async Task<TEntity?> FetchById(TKey id, TEntity? projection = null, CancellationToken cancellationToken = default)
    {
        var context = Enter(RepositoryOperation.FetchById);
        projection ??= Inner.OwnFieldsProjection();
        Policy.SanitizeProjection?.Invoke(context, projection);
        TEntity? found;
        if (Policy.Scope is not null)
        {
            // Through the criteria, so the reach applies.
            var criteria = Policy.KeyCriteria(context, id);
            criteria.Projection = projection;
            var rows = await Inner.Fetch(criteria, 0, 1, cancellationToken);
            found = rows.Count == 0 ? null : rows[0];
        }
        else
        {
            found = await Inner.FetchById(id, projection, cancellationToken);
        }

        if (found is not null)
        {
            Policy.SanitizeOutput?.Invoke(context, found);
        }

        return found;
    }

    // :: Writes

    public async Task<bool> Insert(TEntity bean, TEntity? projection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        var context = Enter(RepositoryOperation.Insert);
        var inserted = await Protection.Write(context.Actor, async () =>
        {
            if (Policy.BeforeInsert is { } before)
            {
                await before(context, bean, cancellationToken);
            }

            return await Inner.Insert(bean, projection, cancellationToken);
        }, cancellationToken);
        if (inserted)
        {
            Policy.SanitizeOutput?.Invoke(context, bean);
        }

        return inserted;
    }

    public Task<bool> Update(TEntity newBean, TEntity? oldBean = null, TEntity? projection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newBean);
        var context = Enter(RepositoryOperation.Update);
        if (Policy.Model.KeyOf(newBean) is not { } key)
        {
            throw new InvalidRequestException($"update of {Policy.Model.Name} without its key");
        }

        var written = projection
            ?? (oldBean is null ? Inner.OwnFieldsProjection() : Policy.Model.ChangedFields(newBean, oldBean));
        return Protection.Write(context.Actor, async () =>
        {
            await RequireWithinReach(context, (TKey)key, cancellationToken);
            if (Policy.BeforeUpdate is { } before)
            {
                await before(context, newBean, written, cancellationToken);
            }

            return await Inner.Update(newBean, oldBean, projection, cancellationToken);
        }, cancellationToken);
    }

    public Task<int> Delete(TCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var context = Enter(RepositoryOperation.Delete);
        if (!criteria.HasFilter)
        {
            // Checked before the reach is added: "delete everything I can reach" is not a use case either.
            throw new InvalidRequestException($"delete of {Policy.Model.Name} without a filter is refused");
        }

        Policy.Scope?.Invoke(context, criteria);
        return Protection.Write(context.Actor, async () =>
        {
            if (Policy.BeforeDelete is { } before)
            {
                await before(context, criteria, cancellationToken);
            }

            return await Inner.Delete(criteria, cancellationToken);
        }, cancellationToken);
    }

    // :: As the policy of a child written in cascade

    async Task ICascadePolicy.CheckInsert(Actor actor, object entity, CancellationToken cancellationToken)
    {
        var context = Cascade(actor, RepositoryOperation.Insert);
        if (Policy.BeforeInsert is { } before)
        {
            await before(context, (TEntity)entity, cancellationToken);
        }
    }

    async Task ICascadePolicy.CheckUpdate(Actor actor, object entity, object projection, CancellationToken cancellationToken)
    {
        var context = Cascade(actor, RepositoryOperation.Update);
        if (Policy.Model.KeyOf((TEntity)entity) is { } key)
        {
            await RequireWithinReach(context, (TKey)key, cancellationToken);
        }

        if (Policy.BeforeUpdate is { } before)
        {
            await before(context, (TEntity)entity, (TEntity)projection, cancellationToken);
        }
    }

    async Task ICascadePolicy.CheckDelete(Actor actor, IReadOnlyList<object> keys, CancellationToken cancellationToken)
    {
        var context = Cascade(actor, RepositoryOperation.Delete);
        foreach (var key in keys)
        {
            await RequireWithinReach(context, (TKey)key, cancellationToken);
            if (Policy.BeforeDelete is { } before)
            {
                await before(context, Policy.KeyCriteria(context, (TKey)key), cancellationToken);
            }
        }
    }

    // :: Internals

    /// <summary>Who is acting, if they may do <paramref name="operation"/>.</summary>
    protected PolicyContext Enter(RepositoryOperation operation)
    {
        var context = new PolicyContext(Protection.RequireActor(), InCascade: false);
        Policy.Authorize?.Invoke(context, operation);
        return context;
    }

    private PolicyContext Cascade(Actor actor, RepositoryOperation operation)
    {
        var context = new PolicyContext(actor, InCascade: true);
        Policy.Authorize?.Invoke(context, operation);
        return context;
    }

    private PolicyContext Narrow(RepositoryOperation operation, TCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var context = Enter(operation);
        criteria.Projection ??= Inner.OwnFieldsProjection();
        Policy.SanitizeProjection?.Invoke(context, criteria.Projection);
        Policy.Scope?.Invoke(context, criteria);
        return context;
    }

    private IReadOnlyList<TEntity> Output(PolicyContext context, IReadOnlyList<TEntity> items)
    {
        if (Policy.SanitizeOutput is { } sanitize)
        {
            foreach (var item in items)
            {
                sanitize(context, item);
            }
        }

        return items;
    }

    private async Task RequireWithinReach(PolicyContext context, TKey key, CancellationToken cancellationToken)
    {
        if (Policy.Scope is null)
        {
            return;
        }

        if (await Inner.Count(Policy.KeyCriteria(context, key), cancellationToken) == 0)
        {
            throw new KeyNotFoundException($"No {Policy.Model.Name} {key} within reach.");
        }
    }
}
