namespace Wdc.Domain;

/// <summary>
/// A repository seen by whoever is acting: every operation goes through the entity's rules, and so does every child
/// it writes in cascade. This is what a host hands to a session (in Blazor Server as in the REST server), so the rules
/// hold in every host.
/// </summary>
/// <remarks>
/// <para>The rules are one of two kinds, never both:</para>
/// <list type="bullet">
/// <item>a <see cref="DataPolicy{TEntity, TCriteria}"/>, declared per role: the rows each grant reaches, the fields read
/// (as they are or masked) and written, deletes. A child written in cascade answers to its own policy: inserted, it
/// must be creatable and reached; updated, its changed fields must be updatable; deleted, some grant must delete it.
/// Rules that are not about access (a price from the product, a record that does not change once made) belong to the
/// repository this one wraps;</item>
/// <item>a <see cref="RepositoryPolicy{TEntity,TCriteria,TKey}"/> of hooks, for rules no declaration expresses. A hook
/// may still use a data policy, through <see cref="DataPolicy{TEntity, TCriteria}.AccessFor"/>.</item>
/// </list>
/// <para>Both change the criteria and the projection the caller passed (as the REST server always did with what it read
/// from the wire). Subclass it to protect what an application repository adds (an image).</para>
/// </remarks>
public class ProtectedRepository<TEntity, TCriteria, TKey> : IRepository<TEntity, TCriteria, TKey>, ICascadePolicy
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    private readonly Action<TCriteria, TKey>? _restrictToId;

    /// <summary>Protected by hooks.</summary>
    public ProtectedRepository(IRepository<TEntity, TCriteria, TKey> inner, RepositoryPolicy<TEntity, TCriteria, TKey> policy,
        Protection protection)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(protection);
        Inner = inner;
        Policy = policy;
        Model = policy.Model;
        Protection = protection;
        protection.Add(typeof(TEntity), this);
    }

    /// <summary>Protected by a data policy.</summary>
    /// <param name="inner">The repository protected.</param>
    /// <param name="data">The data policy of the entity.</param>
    /// <param name="restrictToId">Restricts a criteria to one key: how a fetch by key goes through the policy's reach.</param>
    /// <param name="protection">What the protected repositories of the session share.</param>
    public ProtectedRepository(IRepository<TEntity, TCriteria, TKey> inner, DataPolicy<TEntity, TCriteria> data,
        Action<TCriteria, TKey> restrictToId, Protection protection)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(restrictToId);
        ArgumentNullException.ThrowIfNull(protection);
        Inner = inner;
        Data = data;
        Model = data.Model;
        _restrictToId = restrictToId;
        Protection = protection;
        protection.Add(typeof(TEntity), this);
    }

    protected IRepository<TEntity, TCriteria, TKey> Inner { get; }

    /// <summary>The hooks, when the repository is protected by them.</summary>
    protected RepositoryPolicy<TEntity, TCriteria, TKey>? Policy { get; }

    /// <summary>The data policy, when the repository is protected by one.</summary>
    protected DataPolicy<TEntity, TCriteria>? Data { get; }

    protected EntityModel<TEntity> Model { get; }

    protected Protection Protection { get; }

    public TEntity OwnFieldsProjection() => Inner.OwnFieldsProjection();

    // :: Reads

    public Task<int> Count(TCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var context = Enter(RepositoryOperation.Count);
        if (Data is not null)
        {
            Data.AccessFor(context.Actor).RestrictCount(criteria);
        }
        else
        {
            Policy!.Scope?.Invoke(context, criteria);
        }

        return Inner.Count(criteria, cancellationToken);
    }

    public async Task<IReadOnlyList<TEntity>> Fetch(TCriteria criteria, int offset = 0, int limit = 0,
        CancellationToken cancellationToken = default)
    {
        var (context, plan) = Narrow(RepositoryOperation.Fetch, criteria);
        return await Output(context, plan, await Inner.Fetch(criteria, offset, limit, cancellationToken), cancellationToken);
    }

    public async Task<Page<TEntity>> FetchPage(TCriteria criteria, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (context, plan) = Narrow(RepositoryOperation.FetchPage, criteria);
        var found = await Inner.FetchPage(criteria, page, pageSize, cancellationToken);
        await Output(context, plan, found.Items, cancellationToken);
        return found;
    }

    public async Task<TEntity?> FetchById(TKey id, TEntity? projection = null, CancellationToken cancellationToken = default)
    {
        var context = Enter(RepositoryOperation.FetchById);
        if (Data is not null)
        {
            // Through the criteria, so the reach applies and the row is shaped as any read.
            var criteria = Data.Entity.NewCriteria();
            _restrictToId!(criteria, id);
            criteria.Projection = projection;
            var plan = Data.AccessFor(context.Actor).PrepareRead(criteria);
            var rows = await Inner.Fetch(criteria, 0, 1, cancellationToken);
            return rows.Count == 0 ? null : await plan.Apply(rows[0], cancellationToken);
        }

        var policy = Policy!;
        projection ??= Inner.OwnFieldsProjection();
        policy.SanitizeProjection?.Invoke(context, projection);
        TEntity? found;
        if (policy.Scope is not null)
        {
            // Through the criteria, so the reach applies.
            var criteria = policy.KeyCriteria(context, id);
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
            policy.SanitizeOutput?.Invoke(context, found);
        }

        return found;
    }

    // :: Writes

    public async Task<bool> Insert(TEntity bean, TEntity? projection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        var context = Enter(RepositoryOperation.Insert);
        var access = Data?.AccessFor(context.Actor);
        var inserted = await Protection.Write(context.Actor, async () =>
        {
            if (access is not null)
            {
                await access.CheckCreate(bean, cancellationToken);
            }
            else if (Policy!.BeforeInsert is { } before)
            {
                await before(context, bean, cancellationToken);
            }

            return await Protection.Guarded(context.Actor, () => Inner.Insert(bean, projection, cancellationToken));
        }, cancellationToken);
        if (inserted)
        {
            if (access is not null)
            {
                await access.ReadBack(bean, cancellationToken);
            }
            else
            {
                Policy!.SanitizeOutput?.Invoke(context, bean);
            }
        }

        return inserted;
    }

    public Task<bool> Update(TEntity newBean, TEntity? oldBean = null, TEntity? projection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newBean);
        var context = Enter(RepositoryOperation.Update);
        if (Model.KeyOf(newBean) is not { } key)
        {
            throw new InvalidRequestException($"update of {Model.Name} without its key");
        }

        var written = projection ?? (oldBean is null ? Model.HeldFields(newBean) : Model.ChangedFields(newBean, oldBean));
        return Protection.Write(context.Actor, async () =>
        {
            if (Data is not null)
            {
                await Data.AccessFor(context.Actor).CheckUpdate(newBean, written,
                    (row, scope, ct) => Inner.FetchById((TKey)Model.KeyOf(row)!, scope, ct), cancellationToken);
            }
            else
            {
                await RequireWithinReach(context, (TKey)key, cancellationToken);
                if (Policy!.BeforeUpdate is { } before)
                {
                    await before(context, newBean, written, cancellationToken);
                }
            }

            return await Protection.Guarded(context.Actor, () => Inner.Update(newBean, oldBean, projection, cancellationToken));
        }, cancellationToken);
    }

    public Task<int> Delete(TCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var context = Enter(RepositoryOperation.Delete);
        if (!criteria.HasFilter)
        {
            // Checked before the reach is added: "delete everything I can reach" is not a use case either.
            throw new InvalidRequestException($"delete of {Model.Name} without a filter is refused");
        }

        if (Data is not null)
        {
            Data.AccessFor(context.Actor).RestrictDelete(criteria);
        }
        else
        {
            Policy!.Scope?.Invoke(context, criteria);
        }

        return Protection.Write(context.Actor, async () =>
        {
            if (Policy?.BeforeDelete is { } before)
            {
                await before(context, criteria, cancellationToken);
            }

            return await Protection.Guarded(context.Actor, () => Inner.Delete(criteria, cancellationToken));
        }, cancellationToken);
    }

    // :: As the policy of a child written in cascade

    async Task ICascadePolicy.CheckInsert(Actor actor, object entity, CancellationToken cancellationToken)
    {
        var context = Cascade(actor, RepositoryOperation.Insert);
        if (Data is not null)
        {
            await Data.AccessFor(actor).CheckCreateInCascade((TEntity)entity, cancellationToken);
        }
        else if (Policy!.BeforeInsert is { } before)
        {
            await before(context, (TEntity)entity, cancellationToken);
        }
    }

    async Task ICascadePolicy.CheckUpdate(Actor actor, object entity, object projection, CancellationToken cancellationToken)
    {
        var context = Cascade(actor, RepositoryOperation.Update);
        var child = (TEntity)entity;
        if (Data is not null)
        {
            if (Model.KeyOf(child) is not { } id)
            {
                return;
            }

            // What changes is what the cascade writes and the stored row does not hold already (the parent it points to
            // is the same one).
            var access = Data.AccessFor(actor);
            var scope = access.ScopeProjection();
            foreach (var field in Model.Fields)
            {
                if (!field.IsKey && field.HasValue((TEntity)projection))
                {
                    field.MarkOwnFields(scope);
                }
            }

            var stored = await Inner.FetchById((TKey)id, scope, cancellationToken);
            if (stored is null)
            {
                return;
            }

            var changes = Model.New();
            foreach (var field in Model.Fields)
            {
                if (!field.IsKey && field.HasValue((TEntity)projection) && field.Differs(child, stored))
                {
                    field.MarkOwnFields(changes);
                }
            }

            if (!Model.IsKeyOnly(changes)) // a child sent as it is stored changes nothing, and needs no grant
            {
                await access.CheckUpdate(child, changes, (_, _, _) => Task.FromResult<TEntity?>(stored), cancellationToken);
            }

            return;
        }

        if (Model.KeyOf(child) is { } key)
        {
            await RequireWithinReach(context, (TKey)key, cancellationToken);
        }

        if (Policy!.BeforeUpdate is { } before)
        {
            await before(context, child, (TEntity)projection, cancellationToken);
        }
    }

    async Task ICascadePolicy.CheckDelete(Actor actor, IReadOnlyList<object> keys, CancellationToken cancellationToken)
    {
        var context = Cascade(actor, RepositoryOperation.Delete);
        if (Data is not null)
        {
            var access = Data.AccessFor(actor);
            foreach (var key in keys)
            {
                if (await Inner.FetchById((TKey)key, access.ScopeProjection(), cancellationToken) is { } stored)
                {
                    await access.CheckDeleteInCascade(stored, cancellationToken);
                }
            }

            return;
        }

        var policy = Policy!;
        foreach (var key in keys)
        {
            await RequireWithinReach(context, (TKey)key, cancellationToken);
            if (policy.BeforeDelete is { } before)
            {
                await before(context, policy.KeyCriteria(context, (TKey)key), cancellationToken);
            }
        }
    }

    // :: Internals

    /// <summary>Who is acting, if they may do <paramref name="operation"/>.</summary>
    protected PolicyContext Enter(RepositoryOperation operation)
    {
        var context = new PolicyContext(Protection.RequireActor(), InCascade: false);
        Policy?.Authorize?.Invoke(context, operation);
        return context;
    }

    private PolicyContext Cascade(Actor actor, RepositoryOperation operation)
    {
        var context = new PolicyContext(actor, InCascade: true);
        Policy?.Authorize?.Invoke(context, operation);
        return context;
    }

    private (PolicyContext Context, ReadPlan<TEntity>? Plan) Narrow(RepositoryOperation operation, TCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var context = Enter(operation);
        if (Data is not null)
        {
            return (context, Data.AccessFor(context.Actor).PrepareRead(criteria));
        }

        criteria.Projection ??= Inner.OwnFieldsProjection();
        Policy!.SanitizeProjection?.Invoke(context, criteria.Projection);
        Policy.Scope?.Invoke(context, criteria);
        return (context, null);
    }

    private async Task<IReadOnlyList<TEntity>> Output(PolicyContext context, ReadPlan<TEntity>? plan, IReadOnlyList<TEntity> items,
        CancellationToken cancellationToken)
    {
        if (plan is not null)
        {
            await plan.ApplyAll(items, cancellationToken);
        }
        else if (Policy!.SanitizeOutput is { } sanitize)
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
        var policy = Policy!;
        if (policy.Scope is null)
        {
            return;
        }

        if (await Inner.Count(policy.KeyCriteria(context, key), cancellationToken) == 0)
        {
            throw new KeyNotFoundException($"No {Model.Name} {key} within reach.");
        }
    }
}
