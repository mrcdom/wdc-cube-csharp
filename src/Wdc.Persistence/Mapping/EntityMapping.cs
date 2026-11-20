using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>
/// Binds an <see cref="EntityModel{TEntity}"/> to a table, once: which column holds each field, how references and
/// collections relate, how each named ordering sorts, and the repository's translation of its criteria. From it the repository builds
/// a single statement that returns each row as one JSON object, nested references and collections included, as
/// correlated subqueries: no N+1, no join explosion, only the projected columns.
/// </summary>
public sealed class EntityMapping<TEntity>
    where TEntity : class
{
    private readonly Func<ICriteria, FilterContext, ISqlFragment?>? _conditions;
    private readonly Func<string?, IReadOnlyList<SortSpec>> _ordering;

    internal EntityMapping(EntityModel<TEntity> model, string table, string aliasPrefix, string keyColumn, bool keyGenerated,
        IReadOnlyList<Binding<TEntity>> bindings, Func<ICriteria, FilterContext, ISqlFragment?>? conditions,
        Func<string?, IReadOnlyList<SortSpec>> ordering)
    {
        Model = model;
        Table = table;
        AliasPrefix = aliasPrefix;
        KeyColumn = keyColumn;
        KeyGenerated = keyGenerated;
        Bindings = bindings;
        _conditions = conditions;
        _ordering = ordering;
        foreach (var binding in bindings)
        {
            binding.Owner = this;
        }
    }

    public EntityModel<TEntity> Model { get; }

    public string Table { get; }

    public string AliasPrefix { get; }

    public string KeyColumn { get; }

    /// <summary>Whether the database generates the key (identity); then an insert never writes it.</summary>
    public bool KeyGenerated { get; }

    internal IReadOnlyList<Binding<TEntity>> Bindings { get; }

    internal Binding<TEntity>? BindingOf(FieldModel<TEntity> field) => Bindings.FirstOrDefault(b => ReferenceEquals(b.Field, field));

    /// <summary>Writes the JSON object of one row of <paramref name="alias"/>, shaped by <paramref name="projection"/>; the key always comes.</summary>
    internal void WriteJson(SqlWriter writer, string alias, TEntity projection)
    {
        var entries = new List<JsonEntry>();
        foreach (var binding in Bindings)
        {
            if (binding.Field.IsKey || binding.Field.HasValue(projection))
            {
                binding.AddJsonEntry(entries, alias, projection);
            }
        }

        writer.Dialect.WriteJsonObject(writer, entries);
    }

    /// <summary>
    /// The WHERE condition of <paramref name="criteria"/> on <paramref name="alias"/>, translated by the repository's
    /// <c>Conditions</c> (the same translation serves its own queries and every relation that reaches it);
    /// <see langword="null"/> when nothing filters.
    /// </summary>
    internal ISqlFragment? Conditions(ICriteria? criteria, string alias)
    {
        if (criteria is null || !criteria.HasFilter)
        {
            return null;
        }

        var conditions = _conditions
            ?? throw new InvalidOperationException($"The mapping of {Model.Name} declares no Conditions: it cannot filter.");
        return Sql.Raw(w =>
        {
            var context = new FilterContext(alias, w);
            var fragment = conditions(criteria, context);
            var uncovered = context.Uncovered(criteria).Select(c => c.Name).ToList();
            if (uncovered.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The conditions of {Model.Name} do not translate {string.Join(", ", uncovered.Select(n => $"'{n}'"))}: " +
                    "add them to its repository's Conditions.");
            }

            (fragment ?? Sql.True).Render(w);
        });
    }

    /// <summary>The ORDER BY columns of the criteria's named ordering, with the key as tie-breaker.</summary>
    internal void WriteOrderBy(SqlWriter writer, string alias, ICriteria? criteria) =>
        writer.Join(", ", OrderSpecs(criteria), (w, s) => w.Column(alias, s.Column).Append(s.Descending ? " DESC" : " ASC"));

    internal IReadOnlyList<SortSpec> OrderSpecs(ICriteria? criteria)
    {
        var specs = _ordering(criteria?.OrderByName).ToList();
        if (!specs.Exists(s => s.Column == KeyColumn))
        {
            specs.Add(SortSpec.Asc(KeyColumn));
        }

        return specs;
    }
}

/// <summary>Starts a mapping.</summary>
public static class EntityMapping
{
    public static EntityMappingBuilder<TEntity> For<TEntity>(EntityModel<TEntity> model, string table, string aliasPrefix)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(aliasPrefix);
        return new EntityMappingBuilder<TEntity>(model, table, aliasPrefix);
    }
}

/// <summary>How one field is stored.</summary>
internal abstract class Binding<TEntity>(FieldModel<TEntity> field)
    where TEntity : class
{
    public FieldModel<TEntity> Field { get; } = field;

    public EntityMapping<TEntity> Owner { get; set; } = null!;

    public abstract void AddJsonEntry(List<JsonEntry> entries, string alias, TEntity projection);

    // :: Cascade. Each returns whether it wrote something.

    /// <summary>Before the row is written: a referenced row the projection marks beyond its key.</summary>
    public virtual Task<bool> BeforeWrite(Database database, TEntity bean, TEntity projection, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>After the row is inserted: the children of a collection.</summary>
    public virtual Task<bool> AfterInsert(Database database, TEntity bean, TEntity? projection, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>After the row is updated: a marked collection, as its full state.</summary>
    public virtual Task<bool> AfterUpdate(Database database, TEntity bean, TEntity projection, object parentKey,
        CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>Before rows are deleted: the children of a collection declared as cascade delete.</summary>
    public virtual Task BeforeDelete(Database database, IReadOnlyList<object> keys, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public virtual bool CascadesDelete => false;
}

internal sealed class ScalarBinding<TEntity>(FieldModel<TEntity> field, string column) : Binding<TEntity>(field)
    where TEntity : class
{
    public string Column { get; } = column;

    public IValueKind Kind { get; } = field.ScalarKind!;

    public override void AddJsonEntry(List<JsonEntry> entries, string alias, TEntity projection) =>
        entries.Add(new JsonEntry(Field.Name, w => w.Column(alias, Column), Kind));
}

/// <summary>A binding stored in a foreign-key column of the entity's own table.</summary>
internal interface IReferenceColumn<in TEntity>
{
    string ForeignKeyColumn { get; }

    /// <summary>The child's key in database form, or null.</summary>
    object? ChildKeyOf(TEntity bean);

    /// <summary>The kind of the child's key (the foreign key column's type).</summary>
    IValueKind ChildKeyKind { get; }
}

internal sealed class ReferenceBinding<TEntity, TChild>(
    ReferenceField<TEntity, TChild> field, string foreignKeyColumn, Func<EntityMapping<TChild>> child)
    : Binding<TEntity>(field), IReferenceColumn<TEntity>
    where TEntity : class
    where TChild : class
{
    public object? ChildKeyOf(TEntity bean) =>
        Reference.Get(bean) is { } child ? Child.Model.Key!.ScalarKind!.ToDatabaseObject(Child.Model.KeyOf(child)) : null;

    public IValueKind ChildKeyKind => Child.Model.Key!.ScalarKind!;

    private readonly Lazy<EntityMapping<TChild>> _child = new(child);

    public string ForeignKeyColumn { get; } = foreignKeyColumn;

    public EntityMapping<TChild> Child => _child.Value;

    public ReferenceField<TEntity, TChild> Reference { get; } = field;

    /// <summary>
    /// A projection that marks the referenced entity beyond its key writes it too: inserted when it has no key (the
    /// foreign key then points at it), updated in the marked fields otherwise. Key only: just the foreign key.
    /// </summary>
    public override async Task<bool> BeforeWrite(Database database, TEntity bean, TEntity projection, CancellationToken cancellationToken)
    {
        if (Reference.Get(projection) is not { } shape || Child.Model.IsKeyOnly(shape) || Reference.Get(bean) is not { } child)
        {
            return false;
        }

        var store = database.StoreOf(Child);
        var guard = Wdc.Domain.CascadeGuard.Current;
        if (Child.Model.KeyOf(child) is null)
        {
            if (guard is not null)
            {
                await guard.BeforeInsert(typeof(TChild), child, cancellationToken).ConfigureAwait(false);
            }

            return await store.InsertRow(child, null, cancellationToken).ConfigureAwait(false); // a new row: every field that holds a value
        }

        if (guard is not null)
        {
            await guard.BeforeUpdate(typeof(TChild), child, shape, cancellationToken).ConfigureAwait(false);
        }

        return await store.UpdateRow(child, shape, cancellationToken).ConfigureAwait(false);
    }

    public override void AddJsonEntry(List<JsonEntry> entries, string alias, TEntity projection)
    {
        var childProjection = Reference.Get(projection) ?? Child.Model.KeyProjection();
        var child = Child;
        if (child.Model.IsKeyOnly(childProjection))
        {
            // Only the key was asked: it is already in this row.
            var keyKind = child.Model.Key!.ScalarKind!;
            entries.Add(new JsonEntry(Field.Name,
                w => w.Dialect.WriteKeyObject(w, child.Model.Key.Name, ww => ww.Column(alias, ForeignKeyColumn), keyKind), null));
            return;
        }

        entries.Add(new JsonEntry(Field.Name, w =>
        {
            var childAlias = w.NextAlias(child.AliasPrefix);
            w.Append("(SELECT ");
            child.WriteJson(w, childAlias, childProjection);
            w.Append(" FROM ").Table(child.Table).Append(' ').Identifier(childAlias)
                .Append(" WHERE ").Column(childAlias, child.KeyColumn).Append(" = ").Column(alias, ForeignKeyColumn)
                .Append(')');
        }, null));
    }
}

internal sealed class CollectionBinding<TEntity, TChild>(
    CollectionField<TEntity, TChild> field, string childForeignKeyColumn, Func<EntityMapping<TChild>> child, bool cascadeDelete)
    : Binding<TEntity>(field)
    where TEntity : class
    where TChild : class
{
    private readonly Lazy<EntityMapping<TChild>> _child = new(child);

    public override bool CascadesDelete { get; } = cascadeDelete;

    /// <summary>The child's reference to the parent, mapped on the same column: how a child is pointed at its parent.</summary>
    private ReferenceBinding<TChild, TEntity> Back =>
        Child.Bindings.OfType<ReferenceBinding<TChild, TEntity>>().FirstOrDefault(r => r.ForeignKeyColumn == ChildForeignKeyColumn)
        ?? throw new InvalidOperationException(
            $"To write {Owner.Model.Name}.{Field.Name} in cascade, {Child.Model.Name} must map a reference to " +
            $"{Owner.Model.Name} on '{ChildForeignKeyColumn}'.");

    /// <summary>What to write of each child: the shape of the projection's list, plus the reference to the parent.</summary>
    private TChild ChildProjection(TEntity projection)
    {
        var list = Collection.Get(projection);
        var shape = (list as ProjectionList<TChild>)?.Shape ?? (list is { Count: > 0 } ? list[0] : Child.Model.OwnFieldsProjection());
        var marks = Child.Model.New();
        foreach (var f in Child.Model.Fields)
        {
            f.Copy(shape, marks);
        }

        Back.Reference.Set(marks, Owner.Model.KeyProjection());
        return marks;
    }

    /// <summary>Inserts the children: the marked collection, or (without a projection) the collection that holds a value.</summary>
    public override async Task<bool> AfterInsert(Database database, TEntity bean, TEntity? projection, CancellationToken cancellationToken)
    {
        if ((projection is not null && Collection.Get(projection) is null) || Collection.Get(bean) is not { Count: > 0 } children)
        {
            return false;
        }

        var store = database.StoreOf(Child);
        var guard = Wdc.Domain.CascadeGuard.Current;
        var back = Back.Reference;
        foreach (var item in children)
        {
            back.Set(item, bean);
            if (guard is not null)
            {
                await guard.BeforeInsert(typeof(TChild), item, cancellationToken).ConfigureAwait(false);
            }

            // A new row writes every field that holds a value, the parent's key and what the
            // child's rules filled in included; the projection's shape says what to update in the children that exist.
            await store.InsertRow(item, null, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// The list is the full state of the collection: children without a key are inserted, children with one are
    /// updated in the marked fields, and children of this parent that did not come are deleted (orphans).
    /// </summary>
    public override async Task<bool> AfterUpdate(Database database, TEntity bean, TEntity projection, object parentKey,
        CancellationToken cancellationToken)
    {
        if (Collection.Get(projection) is null)
        {
            return false;
        }

        var store = database.StoreOf(Child);
        var guard = Wdc.Domain.CascadeGuard.Current;
        var childProjection = ChildProjection(projection);
        var back = Back.Reference;
        var existing = await store.KeysWhere(ChildForeignKeyColumn, [parentKey], cancellationToken).ConfigureAwait(false);
        var kept = new HashSet<object>();
        var changed = false;
        foreach (var item in Collection.Get(bean) ?? [])
        {
            back.Set(item, bean);
            if (Child.Model.KeyOf(item) is { } key)
            {
                kept.Add(key);
                if (guard is not null)
                {
                    await guard.BeforeUpdate(typeof(TChild), item, childProjection, cancellationToken).ConfigureAwait(false);
                }

                changed |= await store.UpdateRow(item, childProjection, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (guard is not null)
                {
                    await guard.BeforeInsert(typeof(TChild), item, cancellationToken).ConfigureAwait(false);
                }

                changed |= await store.InsertRow(item, null, cancellationToken).ConfigureAwait(false); // a new row: every valued field
            }
        }

        var orphans = existing.Where(k => !kept.Contains(k)).ToList();
        if (orphans.Count > 0)
        {
            if (guard is not null)
            {
                await guard.BeforeDelete(typeof(TChild), orphans, cancellationToken).ConfigureAwait(false);
            }

            await store.DeleteByKeys(orphans, cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        return changed;
    }

    /// <summary>Deletes the children of the parents about to be deleted, when the collection is declared as cascade delete.</summary>
    public override async Task BeforeDelete(Database database, IReadOnlyList<object> keys, CancellationToken cancellationToken)
    {
        if (!CascadesDelete)
        {
            return;
        }

        var store = database.StoreOf(Child);
        var parentKind = Owner.Model.Key!.ScalarKind!;
        var children = await store.KeysWhere(ChildForeignKeyColumn, [.. keys.Select(k => parentKind.ToDatabaseObject(k)!)],
            cancellationToken).ConfigureAwait(false);
        if (children.Count == 0)
        {
            return;
        }

        if (Wdc.Domain.CascadeGuard.Current is { } guard)
        {
            await guard.BeforeDelete(typeof(TChild), children, cancellationToken).ConfigureAwait(false);
        }

        await store.DeleteByKeys(children, cancellationToken).ConfigureAwait(false);
    }

    public string ChildForeignKeyColumn { get; } = childForeignKeyColumn;

    public EntityMapping<TChild> Child => _child.Value;

    public CollectionField<TEntity, TChild> Collection { get; } = field;

    public override void AddJsonEntry(List<JsonEntry> entries, string alias, TEntity projection)
    {
        var list = Collection.Get(projection);
        var shape = list is { Count: > 0 } ? list[0] : Child.Model.OwnFieldsProjection();
        var slice = list as ProjectionList<TChild>;
        var criteria = slice?.Criteria as ICriteria;
        var sliced = slice?.Limit is not null || slice?.Offset is not null;
        var child = Child;
        var parentKey = Owner.KeyColumn;

        entries.Add(new JsonEntry(Field.Name, w =>
        {
            var childAlias = w.NextAlias(child.AliasPrefix);
            var order = child.OrderSpecs(criteria)
                .Select(s => ((Action<SqlWriter>)(o => o.Column(childAlias, s.Column)), s.Descending))
                .ToList();
            w.Dialect.WriteJsonArraySubquery(w,
                element => child.WriteJson(element, childAlias, shape),
                order,
                from =>
                {
                    from.Append(" FROM ").Table(child.Table).Append(' ').Identifier(childAlias).Append(" WHERE ");
                    Sql.And([
                        Sql.Equal(Sql.Column(childAlias, ChildForeignKeyColumn), Sql.Column(alias, parentKey)),
                        child.Conditions(criteria, childAlias),
                        sliced ? SliceCondition(childAlias, alias, parentKey, criteria, slice!) : null,
                    ])!.Render(from);
                });
        }, null));
    }

    // The slice is applied by key: pk IN (SELECT pk ... ORDER BY ... LIMIT/OFFSET), which keeps the correlation to
    // the parent row and works where a derived table would not. Needs a single-column key.
    private ISqlFragment SliceCondition(string childAlias, string parentAlias, string parentKey, ICriteria? criteria,
        ProjectionList<TChild> slice) =>
        Sql.Raw(w =>
        {
            var child = Child;
            var inner = w.NextAlias(child.AliasPrefix);
            w.Column(childAlias, child.KeyColumn).Append(" IN (SELECT ").Column(inner, child.KeyColumn)
                .Append(" FROM ").Table(child.Table).Append(' ').Identifier(inner).Append(" WHERE ");
            Sql.And([
                Sql.Equal(Sql.Column(inner, ChildForeignKeyColumn), Sql.Column(parentAlias, parentKey)),
                child.Conditions(criteria, inner),
            ])!.Render(w);
            w.Append(" ORDER BY ");
            child.WriteOrderBy(w, inner, criteria);
            w.Dialect.WriteSlice(w, slice.Limit, slice.Offset);
            w.Append(')');
        });
}
