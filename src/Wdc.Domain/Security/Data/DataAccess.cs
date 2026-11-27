namespace Wdc.Domain;

/// <summary>
/// What one actor may do with an entity: the grants of their roles, applied to each request before it reaches the
/// repository, and to each row on its way out. Obtained from <see cref="DataPolicy{TEntity, TCriteria}.AccessFor"/>.
/// </summary>
/// <remarks>
/// <para>Each method either adapts the request (restricts the rows, completes the projection) or refuses it with an
/// <see cref="AccessDeniedException"/> naming the fields at fault. Refusing is deliberate: a client whose request is
/// quietly trimmed shows blanks without knowing why, and a correct client never asks for what its roles do not
/// allow.</para>
/// <list type="bullet">
/// <item><b>Reading</b> (<see cref="PrepareRead"/>): every projected field must be seen by some grant; every field the
/// criteria filters or sorts by must be read as it is by one grant, and only the rows of the grants that do so are
/// returned, as alternative groups joined with what was asked. Each row then shows, per field, what the grants that
/// reach it allow: as it is, masked, or nothing. Inside a reference or a collection, the policy of that entity decides,
/// row by row, and restricts the rows of a collection too. Without a projection, the default shape, trimmed to what may
/// be read.</item>
/// <item><b>Counting</b> (<see cref="RestrictCount"/>): the same rows as reading.</item>
/// <item><b>Creating</b> (<see cref="CheckCreate(TEntity, CancellationToken)"/>): one grant must allow every field sent and reach the new row; a
/// reference is written by its key only, and each row of a collection is created through its entity's policy.</item>
/// <item><b>Updating</b> (<see cref="CheckUpdate"/>): one grant must reach the stored row, allow every changed field,
/// and still reach the row after the change.</item>
/// <item><b>Deleting</b> (<see cref="RestrictDelete"/>): only the rows of grants that delete, filtered by fields they
/// read.</item>
/// </list>
/// <para>Without an actor (no security) the writes are not checked and every row is reached; a read without a
/// projection keeps the repository's default, and the write-only fields are still refused and never returned.</para>
/// </remarks>
public sealed class DataAccess<TEntity, TCriteria> : IDataAccess
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    private readonly PolicyEntity<TEntity, TCriteria> _entity;
    private readonly Actor? _actor;
    private readonly List<DataGrant<TEntity, TCriteria>> _grants;

    internal DataAccess(PolicyEntity<TEntity, TCriteria> entity, Actor? actor, List<DataGrant<TEntity, TCriteria>> grants)
    {
        _entity = entity;
        _actor = actor;
        _grants = grants;
    }

    /// <summary>The grants in force: those of every role of the actor.</summary>
    public IReadOnlyList<DataGrant<TEntity, TCriteria>> Grants => _grants;

    // :: Reading

    /// <summary>
    /// Adapts a read: checks the projection and the filtered fields, restricts the rows, and sets on the criteria the
    /// projection to fetch: what was asked, plus what the row checks read.
    /// </summary>
    /// <remarks>Without a projection the read gets the default shape, <b>trimmed</b> to what the actor may read instead
    /// of refused: the client asked for nothing in particular. Without an actor it is left without one (the repository's
    /// default), and only the write-only fields are cleared from the rows.</remarks>
    /// <returns>The plan that shapes each returned row: apply it before the rows leave.</returns>
    /// <exception cref="AccessDeniedException">A field is projected, filtered or sorted by that no grant allows.</exception>
    public ReadPlan<TEntity> PrepareRead(TCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var requested = criteria.Projection;
        if (_actor is null && requested is null)
        {
            var writeOnly = _entity.Fields.Where(f => _entity.IsWriteOnly(f.Name)).ToList();
            return new ReadPlan<TEntity>((row, _) =>
            {
                foreach (var field in writeOnly)
                {
                    field.SetValue(row, null);
                }

                return ValueTask.FromResult(row);
            });
        }

        var shape = requested is not null ? Plan(requested, criteria, trim: false) : Plan(_entity.DefaultShape(), criteria, trim: true);
        criteria.Projection = shape.Fetch;
        return new ReadPlan<TEntity>(async (row, ct) =>
        {
            await shape.Apply(row, NewShaping(), ct);
            return row;
        });
    }

    /// <summary>Restricts a count to the rows a read with the same criteria would return.</summary>
    /// <exception cref="AccessDeniedException">A field is filtered by that no grant reads.</exception>
    public void RestrictCount(TCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        RestrictRows(criteria, EligibleToFilter(criteria, Readers()));
    }

    /// <summary>
    /// Shapes a row the server wrote (what an insert answers with) as a read of the fields it carries would: what the
    /// actor may not read leaves it, nothing is refused. An actor who reads nothing of the entity gets the key.
    /// </summary>
    public async ValueTask<TEntity> ReadBack(TEntity row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (ReadsNothing)
        {
            _entity.KeepKeyOnly(row);
            return row;
        }

        await Plan(_entity.ProjectionMirroring(row, NewShaping()), null, trim: true).Apply(row, NewShaping(), cancellationToken);
        return row;
    }

    /// <summary>Whether no grant reads this entity: then, in a default shape, a reference to it goes by key, a collection out.</summary>
    private bool ReadsNothing => !_grants.Exists(g => g.Reads);

    /// <summary>
    /// One level of a read: what is asked of this entity, the rows the criteria may reach (when there is one; a
    /// reference has none, its row is the one the owner points to), and what to fetch. With <c>trim</c>, what the actor
    /// may not read is left out (a default shape) instead of refused (a projection the client sent).
    /// </summary>
    private Shape Plan(TEntity requested, TCriteria? criteria, bool trim)
    {
        var readers = Readers();
        var fields = _entity.FieldsSetIn(requested);
        var unseen = fields.Where(f => !readers.Exists(g => g.Sees(f))).ToList();
        if (trim)
        {
            fields.RemoveAll(unseen.Contains);
        }
        else if (unseen.Count > 0)
        {
            throw Denied($"not allowed to read {_entity.Name} fields [{string.Join(", ", unseen)}]");
        }

        if (criteria is not null)
        {
            RestrictRows(criteria, EligibleToFilter(criteria, readers));
        }

        var fetch = _entity.Model.KeyProjection();
        var nested = new Dictionary<string, IShape>(StringComparer.Ordinal);
        var unreadable = new List<string>();
        foreach (var name in fields)
        {
            var field = _entity.Field(name);
            var inside = field.GetValue(requested);
            switch (field.Kind)
            {
                case FieldKind.Scalar:
                    _entity.Mark(field, fetch);
                    break;
                case FieldKind.Reference:
                    {
                        var target = _entity.Target(field);
                        var access = target.AccessFor(_actor);
                        if (inside is not null && target.HasFieldsSet(inside) && !(trim && access.ReadsNothing))
                        {
                            var shape = access.Plan(inside, null, trim);
                            field.SetValue(fetch, shape.Fetch);
                            nested[name] = shape;
                        }
                        else
                        {
                            _entity.Mark(field, fetch);
                        }

                        break;
                    }

                default:
                    {
                        var target = _entity.Target(field);
                        var access = target.AccessFor(_actor);
                        var first = inside is System.Collections.IList { Count: > 0 } list ? list[0] : null;
                        if (trim && access.ReadsNothing)
                        {
                            unreadable.Add(name);
                        }
                        else if (first is not null)
                        {
                            var envelope = inside as IProjectionList;
                            var childCriteria = envelope?.Criteria as ICriteria ?? target.NewCriteria();
                            var shape = access.Plan(first, childCriteria, trim);
                            field.SetValue(fetch, target.NewProjectionList(shape.Fetch, childCriteria, envelope?.Limit, envelope?.Offset));
                            nested[name] = shape;
                        }
                        else
                        {
                            _entity.Mark(field, fetch);
                        }

                        break;
                    }
            }
        }

        fields.RemoveAll(unreadable.Contains);
        foreach (var grant in readers)
        {
            _entity.Merge(fetch, grant.Rows.Projection());
        }

        return new Shape(this, fields.ToHashSet(StringComparer.Ordinal), readers, nested, fetch);
    }

    /// <summary>A planned level of a read, and how it shapes each returned row.</summary>
    private sealed class Shape(DataAccess<TEntity, TCriteria> access, HashSet<string> fields, List<DataGrant<TEntity, TCriteria>> readers,
        Dictionary<string, IShape> nested, TEntity fetch) : IShape
    {
        public TEntity Fetch { get; } = fetch;

        object IShape.Fetch => Fetch;

        ValueTask IShape.Apply(object row, HashSet<object> shaping, CancellationToken cancellationToken) =>
            Apply((TEntity)row, shaping, cancellationToken);

        /// <summary>
        /// The field values of a row: as they are, masked, or cleared, by the grants that reach it. A row already being
        /// shaped higher up (a child pointing back to its owner) is left to that level.
        /// </summary>
        public async ValueTask Apply(TEntity row, HashSet<object> shaping, CancellationToken cancellationToken)
        {
            if (!shaping.Add(row))
            {
                return;
            }

            try
            {
                var reaching = new List<DataGrant<TEntity, TCriteria>>();
                foreach (var grant in readers)
                {
                    if (await access.Covers(grant, row, cancellationToken))
                    {
                        reaching.Add(grant);
                    }
                }

                foreach (var field in access._entity.Fields)
                {
                    if (!fields.Contains(field.Name))
                    {
                        field.SetValue(row, null);
                        continue;
                    }

                    var readAsItIs = reaching.Exists(g => g.Read.Contains(field.Name));
                    if (field.Kind != FieldKind.Scalar)
                    {
                        if (readAsItIs)
                        {
                            await ShapeRelated(field, row, shaping, cancellationToken);
                        }
                        else
                        {
                            field.SetValue(row, null);
                        }

                        continue;
                    }

                    if (readAsItIs)
                    {
                        continue;
                    }

                    var mask = reaching.Select(g => g.Masked.GetValueOrDefault(field.Name)).FirstOrDefault(m => m is not null);
                    if (mask is not null && field.GetValue(row) is string text)
                    {
                        field.SetValue(row, mask.Apply(text));
                    }
                    else
                    {
                        field.SetValue(row, null);
                    }
                }
            }
            finally
            {
                shaping.Remove(row);
            }
        }

        /// <summary>The related row (or rows): through the related entity's plan, or reduced to the key when only it was asked.</summary>
        private async ValueTask ShapeRelated(FieldModel<TEntity> field, TEntity row, HashSet<object> shaping, CancellationToken cancellationToken)
        {
            var inside = nested.GetValueOrDefault(field.Name);
            var target = access._entity.Target(field);
            switch (field.GetValue(row))
            {
                case null:
                    break;
                case System.Collections.IList list:
                    foreach (var child in list)
                    {
                        if (child is not null)
                        {
                            await ShapeOne(inside, target, child, shaping, cancellationToken);
                        }
                    }

                    break;
                case var related:
                    await ShapeOne(inside, target, related, shaping, cancellationToken);
                    break;
            }
        }

        private static async ValueTask ShapeOne(IShape? inside, IDataPolicy target, object related, HashSet<object> shaping,
            CancellationToken cancellationToken)
        {
            if (shaping.Contains(related))
            {
                return;
            }

            if (inside is not null)
            {
                await inside.Apply(related, shaping, cancellationToken);
            }
            else
            {
                target.KeepKeyOnly(related);
            }
        }
    }

    // :: Writing

    /// <summary>
    /// Checks a new row: one grant must allow every field set in it and reach it. A reference is written by its key
    /// only; each row of a collection (the items of a new purchase) is checked as a new row of its entity, belonging to
    /// this one.
    /// </summary>
    /// <exception cref="AccessDeniedException">No grant allows it.</exception>
    public ValueTask CheckCreate(TEntity row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _actor is null ? ValueTask.CompletedTask : CheckCreate(row, null, inCascade: false, cancellationToken);
    }

    /// <summary>
    /// Checks a row the repository inserts in cascade, its reference to the parent already set: the fields and the
    /// reach, without the rule on references (a cascade writes them by key) nor the children (the cascade reports them).
    /// </summary>
    internal ValueTask CheckCreateInCascade(TEntity row, CancellationToken cancellationToken) =>
        _actor is null ? ValueTask.CompletedTask : CheckCreate(row, null, inCascade: true, cancellationToken);

    // owner: the reference pointing to the row this one is created inside, set by the owner's check.
    private async ValueTask CheckCreate(TEntity row, string? owner, bool inCascade, CancellationToken cancellationToken)
    {
        var fields = _entity.FieldsSetIn(row);
        var creators = _grants.Where(g => g.Create.Count > 0).ToList();
        if (creators.Count == 0)
        {
            throw Denied($"not allowed to create {_entity.Name}");
        }

        if (!inCascade)
        {
            RequireReferencesByKey(row, fields, owner);
        }

        var allowed = false;
        foreach (var grant in creators)
        {
            if (fields.TrueForAll(grant.Create.Contains) && await Covers(grant, row, cancellationToken))
            {
                allowed = true;
                break;
            }
        }

        if (!allowed)
        {
            throw Denied($"not allowed to create {_entity.Name} with [{string.Join(", ", NotIn(fields, creators, g => g.Create))}]");
        }

        if (inCascade)
        {
            return;
        }

        foreach (var name in fields)
        {
            var field = _entity.Field(name);
            if (field.Kind == FieldKind.Collection)
            {
                await CheckCreateChildren(field, row, cancellationToken);
            }
        }
    }

    private async ValueTask CheckCreateChildren(FieldModel<TEntity> field, TEntity row, CancellationToken cancellationToken)
    {
        var target = _entity.Target(field);
        var inverse = _entity.Inverse(field);
        var access = target.AccessFor(_actor);
        if (field.GetValue(row) is not System.Collections.IList children)
        {
            return;
        }

        foreach (var child in children)
        {
            if (child is null)
            {
                continue;
            }

            // The child belongs to the new row while it is checked; what it carried is given back after.
            var previous = target.GetField(child, inverse);
            target.SetField(child, inverse, row);
            try
            {
                await access.CheckCreate(child, inverse, cancellationToken);
            }
            finally
            {
                target.SetField(child, inverse, previous);
            }
        }
    }

    /// <summary>A reference sent with more than its key would look like a write of the related row, which it is not.</summary>
    private void RequireReferencesByKey(TEntity row, List<string> fields, string? owner)
    {
        foreach (var name in fields)
        {
            var field = _entity.Field(name);
            if (field.Kind == FieldKind.Reference && name != owner && _entity.Target(field).HasFieldsSet(field.GetValue(row)))
            {
                throw Denied($"{_entity.Name}.{name} is written by its key only");
            }
        }
    }

    /// <summary>
    /// Checks an update: one grant must reach the stored row, allow every changed field, and still reach the row once
    /// changed: a grant over one's own rows cannot hand a row to someone else.
    /// </summary>
    /// <remarks>A stored row that no updating grant reaches is answered as not found (<see cref="KeyNotFoundException"/>)
    /// when the actor cannot read it either, as for a row that does not exist: refusing would tell the row is there,
    /// which a read hides. When the actor reads it, the answer is <see cref="AccessDeniedException"/>.</remarks>
    /// <param name="row">The new values, with the key.</param>
    /// <param name="changes">A projection of the changed fields.</param>
    /// <param name="load">Fetches the stored row by the key of <paramref name="row"/> with the given projection;
    /// <see langword="null"/> when it does not exist, and then there is nothing to check.</param>
    /// <param name="cancellationToken">Cancels the check and the load.</param>
    /// <exception cref="InvalidRequestException">The row has no key.</exception>
    public async ValueTask CheckUpdate(TEntity row, TEntity changes, Func<TEntity, TEntity, CancellationToken, Task<TEntity?>> load,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(load);
        if (_actor is null)
        {
            return;
        }

        var fields = _entity.FieldsSetIn(changes);
        var updaters = _grants.Where(g => g.Update.Count > 0).ToList();
        if (updaters.Count == 0)
        {
            throw Denied($"not allowed to update {_entity.Name}");
        }

        if (!_entity.Key.HasValue(row))
        {
            throw new InvalidRequestException($"update of {_entity.Name} without the key");
        }

        if (fields.Count == 0)
        {
            return;
        }

        RequireReferencesByKey(row, fields, null);
        var readers = _grants.Where(g => g.Reads).ToList();
        var scope = ScopeProjection();
        if (await load(row, scope, cancellationToken) is not { } stored)
        {
            return;
        }

        var reaching = new List<DataGrant<TEntity, TCriteria>>();
        foreach (var grant in updaters)
        {
            if (await Covers(grant, stored, cancellationToken))
            {
                reaching.Add(grant);
            }
        }

        if (reaching.Count == 0)
        {
            foreach (var grant in readers)
            {
                if (await Covers(grant, stored, cancellationToken))
                {
                    throw Denied($"not allowed to update this {_entity.Name}");
                }
            }

            throw new KeyNotFoundException("Not found");
        }

        var changed = _entity.Model.New();
        _entity.Key.Copy(stored, changed);
        foreach (var name in _entity.FieldsSetIn(scope))
        {
            _entity.Field(name).Copy(stored, changed);
        }

        foreach (var name in fields)
        {
            _entity.Field(name).Copy(row, changed);
        }

        foreach (var grant in reaching)
        {
            if (fields.TrueForAll(grant.Update.Contains) && await Covers(grant, changed, cancellationToken))
            {
                return;
            }
        }

        throw Denied($"not allowed to update {_entity.Name} [{string.Join(", ", NotIn(fields, reaching, g => g.Update))}]");
    }

    /// <summary>The key and what every grant's row check reads: how to load a stored row to check it.</summary>
    internal TEntity ScopeProjection()
    {
        var scope = _entity.Model.KeyProjection();
        foreach (var grant in _grants)
        {
            _entity.Merge(scope, grant.Rows.Projection());
        }

        return scope;
    }

    /// <summary>Checks a stored row the repository deletes in cascade: some grant that deletes must reach it.</summary>
    internal async ValueTask CheckDeleteInCascade(TEntity stored, CancellationToken cancellationToken)
    {
        if (_actor is null)
        {
            return;
        }

        foreach (var grant in _grants.Where(g => g.Delete))
        {
            if (await Covers(grant, stored, cancellationToken))
            {
                return;
            }
        }

        throw Denied($"not allowed to delete this {_entity.Name}");
    }

    /// <summary>Restricts a delete to the rows of the grants that delete and read every field the criteria filters by.</summary>
    /// <exception cref="AccessDeniedException">No grant deletes, or none reads what the criteria filters by.</exception>
    public void RestrictDelete(TCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (_actor is null)
        {
            return;
        }

        var deleters = _grants.Where(g => g.Delete).ToList();
        if (deleters.Count == 0)
        {
            throw Denied($"not allowed to delete {_entity.Name}");
        }

        RestrictRows(criteria, EligibleToFilter(criteria, deleters));
    }

    // :: Rows

    private List<DataGrant<TEntity, TCriteria>> Readers()
    {
        var readers = _grants.Where(g => g.Reads).ToList();
        return readers.Count > 0 ? readers : throw Denied($"not allowed to read {_entity.Name}");
    }

    /// <summary>
    /// The grants that read, as they are, every field the criteria filters or sorts by: filtering by a field reveals its
    /// values, so it takes a grant that reads it, and only the rows of such grants are returned. A field of another
    /// entity (a path) also takes that entity's policy reading it.
    /// </summary>
    private List<DataGrant<TEntity, TCriteria>> EligibleToFilter(TCriteria criteria, List<DataGrant<TEntity, TCriteria>> candidates)
    {
        var paths = _entity.FilteredPaths(criteria);
        var heads = paths.Select(PolicyEntity<TEntity, TCriteria>.Head).Distinct().ToList();
        var eligible = candidates.Where(g => heads.TrueForAll(g.Read.Contains)).ToList();
        if (eligible.Count == 0)
        {
            throw Denied($"not allowed to filter or sort {_entity.Name} by [{string.Join(", ", NotIn(heads, candidates, g => g.Read))}]");
        }

        foreach (var path in paths)
        {
            var dot = path.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0)
            {
                var field = _entity.Field(path[..dot]);
                _entity.Target(field).AccessFor(_actor).RequireFilterable(path[(dot + 1)..]);
            }
        }

        return eligible;
    }

    /// <summary>Requires some grant to read the field at <paramref name="path"/> as it is (through the references on the way).</summary>
    private void RequireFilterable(string path)
    {
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        var head = dot > 0 ? path[..dot] : path;
        if (!_grants.Exists(g => g.Read.Contains(head)))
        {
            throw Denied($"not allowed to filter or sort {_entity.Name} by [{head}]");
        }

        if (dot > 0)
        {
            _entity.Target(_entity.Field(head)).AccessFor(_actor).RequireFilterable(path[(dot + 1)..]);
        }
    }

    /// <summary>
    /// Adds to the criteria the rows the grants reach: nothing when one of them reaches every row; otherwise one
    /// alternative group per row scope, each carrying the groups the criteria already had, so those alternatives still
    /// hold inside the reachable rows.
    /// </summary>
    private void RestrictRows(TCriteria criteria, List<DataGrant<TEntity, TCriteria>> grants)
    {
        var scopes = new List<IRowScope<TEntity, TCriteria>>();
        foreach (var grant in grants)
        {
            if (grant.Rows is AllRows<TEntity, TCriteria>)
            {
                return;
            }

            if (!scopes.Exists(s => ReferenceEquals(s, grant.Rows)))
            {
                scopes.Add(grant.Rows);
            }
        }

        var existing = criteria.AnyOf.ToArray();
        var groups = scopes.Select(scope =>
        {
            var group = _entity.NewCriteria();
            scope.Restrict(group, _actor!);
            return group.WithAnyOf(existing);
        }).ToArray();
        criteria.WithAnyOf(groups);
    }

    private ValueTask<bool> Covers(DataGrant<TEntity, TCriteria> grant, TEntity row, CancellationToken cancellationToken) =>
        grant.Rows is AllRows<TEntity, TCriteria> ? ValueTask.FromResult(true) : grant.Rows.Covers(row, _actor!, cancellationToken);

    // :: Helpers

    /// <summary>
    /// The fields none of the grants declares; all of <paramref name="fields"/> when each is declared by some grant:
    /// then what fails is asking for them together, and the message names them all.
    /// </summary>
    private static List<string> NotIn(List<string> fields, List<DataGrant<TEntity, TCriteria>> grants,
        Func<DataGrant<TEntity, TCriteria>, IReadOnlySet<string>> declared)
    {
        var missing = fields.Where(f => !grants.Exists(g => declared(g).Contains(f))).ToList();
        return missing.Count > 0 ? missing : fields;
    }

    private static HashSet<object> NewShaping() => new(ReferenceEqualityComparer.Instance);

    private static AccessDeniedException Denied(string message) => new(message);

    bool IDataAccess.ReadsNothing => ReadsNothing;

    IShape IDataAccess.Plan(object requested, ICriteria? criteria, bool trim) => Plan((TEntity)requested, (TCriteria?)criteria, trim);

    ValueTask IDataAccess.CheckCreate(object row, string? owner, CancellationToken cancellationToken) =>
        CheckCreate((TEntity)row, owner, inCascade: false, cancellationToken);

    void IDataAccess.RequireFilterable(string path) => RequireFilterable(path);
}

/// <summary>A data access seen without its types, for the policy of the entity that points to this one.</summary>
internal interface IDataAccess
{
    bool ReadsNothing { get; }

    IShape Plan(object requested, ICriteria? criteria, bool trim);

    ValueTask CheckCreate(object row, string? owner, CancellationToken cancellationToken);

    void RequireFilterable(string path);
}

/// <summary>A planned level of a read, seen without its type.</summary>
internal interface IShape
{
    object Fetch { get; }

    ValueTask Apply(object row, HashSet<object> shaping, CancellationToken cancellationToken);
}
