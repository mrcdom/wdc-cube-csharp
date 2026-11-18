namespace Wdc.Domain;

/// <summary>
/// The fields of an entity, declared once (decision D-10): name, kind, getter and setter. Shared by client and
/// server; the SQL layer binds columns to these fields and the JSON codec reads and writes through them. No reflection.
/// </summary>
/// <remarks>
/// Entities are plain classes whose properties are all nullable: <c>null</c> means "absent" in a result and "not
/// wanted" in a projection (a bean filled with <see cref="Mark"/> values says which fields to fetch or write).
/// </remarks>
public sealed class EntityModel<TEntity> : IEntityModel
    where TEntity : class
{
    private readonly Func<TEntity> _factory;
    private readonly Dictionary<string, FieldModel<TEntity>> _byName;

    internal EntityModel(string name, Func<TEntity> factory, IReadOnlyList<FieldModel<TEntity>> fields)
    {
        Name = name;
        _factory = factory;
        Fields = fields;
        _byName = fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
        Key = fields.FirstOrDefault(f => f.IsKey);
    }

    public string Name { get; }

    public Type EntityType => typeof(TEntity);

    /// <summary>Every field, in declaration order.</summary>
    public IReadOnlyList<FieldModel<TEntity>> Fields { get; }

    /// <summary>The key field, when declared.</summary>
    public FieldModel<TEntity>? Key { get; }

    /// <summary>A new, empty instance.</summary>
    public TEntity New() => _factory();

    /// <summary>The field named <paramref name="name"/>, or <see langword="null"/>.</summary>
    public FieldModel<TEntity>? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// The default projection: own scalar fields (except the ones declared out of it, like a blob or a password)
    /// and the key of each reference. No collections.
    /// </summary>
    public TEntity OwnFieldsProjection()
    {
        var projection = New();
        foreach (var field in Fields)
        {
            if (field.InDefaultProjection && field.Kind != FieldKind.Collection)
            {
                field.MarkOwnFields(projection);
            }
        }

        return projection;
    }

    /// <summary>A projection that asks only for the key.</summary>
    public TEntity KeyProjection()
    {
        var projection = New();
        Key?.MarkOwnFields(projection);
        return projection;
    }

    /// <summary>Whether <paramref name="projection"/> asks for nothing beyond the key.</summary>
    public bool IsKeyOnly(TEntity projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Fields.All(f => f.IsKey || !f.HasValue(projection));
    }

    /// <summary>The key value of <paramref name="entity"/>, boxed, or <see langword="null"/>.</summary>
    public object? KeyOf(TEntity entity) => Key?.GetValue(entity);

    /// <summary>
    /// A projection marking every field (own scalars and references) that differs between <paramref name="next"/>
    /// and <paramref name="previous"/>: what an update must write. The key is always marked.
    /// </summary>
    public TEntity ChangedFields(TEntity next, TEntity previous)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(previous);
        var projection = New();
        foreach (var field in Fields)
        {
            if (field.IsKey || (field.Kind != FieldKind.Collection && field.Differs(next, previous)))
            {
                field.MarkOwnFields(projection);
            }
        }

        return projection;
    }
}

/// <summary>Starts declaring an entity model.</summary>
public static class EntityModel
{
    public static EntityModelBuilder<TEntity> For<TEntity>(Func<TEntity> factory, string? name = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new EntityModelBuilder<TEntity>(name ?? typeof(TEntity).Name, factory);
    }
}
