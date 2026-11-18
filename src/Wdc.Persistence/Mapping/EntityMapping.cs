using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>
/// Binds an <see cref="EntityModel{TEntity}"/> to a table, once: which column holds each field, how references and
/// collections relate, how each criterion filters and how each named ordering sorts. From it the repository builds
/// a single statement that returns each row as one JSON object, nested references and collections included, as
/// correlated subqueries: no N+1, no join explosion, only the projected columns.
/// </summary>
public sealed class EntityMapping<TEntity>
    where TEntity : class
{
    private readonly Dictionary<string, Func<Criterion, FilterContext, ISqlFragment?>> _filters;
    private readonly Func<string?, IReadOnlyList<SortSpec>> _ordering;

    internal EntityMapping(EntityModel<TEntity> model, string table, string aliasPrefix, string keyColumn, bool keyGenerated,
        IReadOnlyList<Binding<TEntity>> bindings, Dictionary<string, Func<Criterion, FilterContext, ISqlFragment?>> filters,
        Func<string?, IReadOnlyList<SortSpec>> ordering)
    {
        Model = model;
        Table = table;
        AliasPrefix = aliasPrefix;
        KeyColumn = keyColumn;
        KeyGenerated = keyGenerated;
        Bindings = bindings;
        _filters = filters;
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

    /// <summary>The WHERE condition of <paramref name="criteria"/> on <paramref name="alias"/>; <see langword="null"/> when nothing filters.</summary>
    internal ISqlFragment? Conditions(ICriteria? criteria, string alias)
    {
        if (criteria is null)
        {
            return null;
        }

        var context = new FilterContext(alias);
        var parts = new List<ISqlFragment?>();
        foreach (var criterion in criteria.Criterions)
        {
            if (!criterion.IsSet && !criterion.IsRestricted)
            {
                continue;
            }

            if (_filters.TryGetValue(criterion.Name, out var filter))
            {
                parts.Add(filter(criterion, context));
            }
            else if (Bindings.OfType<ScalarBinding<TEntity>>().FirstOrDefault(b => b.Field.Name == criterion.Name) is { } scalar)
            {
                parts.Add(Sql.Translate(Sql.Column(alias, scalar.Column), criterion));
            }
            else
            {
                throw new InvalidOperationException(
                    $"The mapping of {Model.Name} does not know how to filter by '{criterion.Name}': declare a Filter for it.");
            }
        }

        return Sql.And(parts);
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
    CollectionField<TEntity, TChild> field, string childForeignKeyColumn, Func<EntityMapping<TChild>> child)
    : Binding<TEntity>(field)
    where TEntity : class
    where TChild : class
{
    private readonly Lazy<EntityMapping<TChild>> _child = new(child);

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
