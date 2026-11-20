using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>Declares a mapping.</summary>
public sealed class EntityMappingBuilder<TEntity>
    where TEntity : class
{
    private readonly EntityModel<TEntity> _model;
    private readonly string _table;
    private readonly string _alias;
    private readonly List<Binding<TEntity>> _bindings = [];
    private Func<ICriteria, FilterContext, ISqlFragment?>? _conditions;
    private Func<string?, IReadOnlyList<SortSpec>> _ordering = static _ => [];
    private string? _keyColumn;
    private bool _keyGenerated;

    internal EntityMappingBuilder(EntityModel<TEntity> model, string table, string alias)
    {
        _model = model;
        _table = table;
        _alias = alias;
    }

    /// <summary>The key's column; <paramref name="generated"/> when the database assigns it (identity).</summary>
    public EntityMappingBuilder<TEntity> Key(string? column = null, bool generated = false)
    {
        var key = _model.Key ?? throw new InvalidOperationException($"{_model.Name} declares no key.");
        _keyColumn = column ?? key.Name;
        _keyGenerated = generated;
        _bindings.Add(new ScalarBinding<TEntity>(key, _keyColumn));
        return this;
    }

    /// <summary>A scalar field's column (the field name by default).</summary>
    public EntityMappingBuilder<TEntity> Column(string field, string? column = null)
    {
        var model = Field(field, FieldKind.Scalar);
        _bindings.Add(new ScalarBinding<TEntity>(model, column ?? field));
        return this;
    }

    /// <summary>A reference held by a foreign-key column of this table, pointing at the child's key.</summary>
    public EntityMappingBuilder<TEntity> Reference<TChild>(string field, string foreignKeyColumn, Func<EntityMapping<TChild>> child)
        where TChild : class
    {
        ArgumentNullException.ThrowIfNull(child);
        var model = (ReferenceField<TEntity, TChild>)Field(field, FieldKind.Reference);
        _bindings.Add(new ReferenceBinding<TEntity, TChild>(model, foreignKeyColumn, child));
        return this;
    }

    /// <summary>
    /// A collection whose children point at this entity's key through their own foreign-key column. With
    /// <paramref name="cascadeDelete"/>, deleting this entity deletes its children first (they belong to it); without
    /// it the collection is a view (the sales of a product) and a delete leaves it alone.
    /// </summary>
    public EntityMappingBuilder<TEntity> Collection<TChild>(string field, string childForeignKeyColumn, Func<EntityMapping<TChild>> child,
        bool cascadeDelete = false)
        where TChild : class
    {
        ArgumentNullException.ThrowIfNull(child);
        var model = (CollectionField<TEntity, TChild>)Field(field, FieldKind.Collection);
        _bindings.Add(new CollectionBinding<TEntity, TChild>(model, childForeignKeyColumn, child, cascadeDelete));
        return this;
    }

    /// <summary>
    /// How the repository translates its criteria (each repository owns it). Every
    /// relation that reaches this entity reuses it on the relation's subquery.
    /// </summary>
    public EntityMappingBuilder<TEntity> Conditions<TCriteria>(Func<TCriteria, FilterContext, ISqlFragment?> conditions)
        where TCriteria : ICriteria
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var name = _model.Name;
        _conditions = (criteria, context) => criteria is TCriteria typed
            ? conditions(typed, context)
            : throw new InvalidOperationException(
                $"The conditions of {name} read {typeof(TCriteria).Name}, not {criteria.GetType().Name}.");
        return this;
    }

    /// <summary>Turns each named ordering into columns (the key is added as tie-breaker).</summary>
    public EntityMappingBuilder<TEntity> Ordering<TOrderBy>(Func<TOrderBy, IReadOnlyList<SortSpec>> columns)
        where TOrderBy : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(columns);
        var byWire = Enum.GetValues<TOrderBy>().ToDictionary(v => OrderByNames.ToWire(v.ToString()), v => v, StringComparer.Ordinal);
        _ordering = name => name is not null && byWire.TryGetValue(name, out var value) ? columns(value) : [];
        return this;
    }

    public EntityMapping<TEntity> Build() =>
        new(_model, _table, _alias, _keyColumn ?? throw new InvalidOperationException($"Map the key of {_model.Name}."),
            _keyGenerated, [.. _bindings], _conditions, _ordering);

    private FieldModel<TEntity> Field(string name, FieldKind kind)
    {
        var field = _model.Find(name) ?? throw new ArgumentException($"{_model.Name} has no field '{name}'.", nameof(name));
        if (field.Kind != kind)
        {
            throw new ArgumentException($"{_model.Name}.{name} is a {field.Kind}, not a {kind}.", nameof(name));
        }

        if (_bindings.Exists(b => ReferenceEquals(b.Field, field)))
        {
            throw new ArgumentException($"{_model.Name}.{name} is mapped twice.", nameof(name));
        }

        return field;
    }
}
