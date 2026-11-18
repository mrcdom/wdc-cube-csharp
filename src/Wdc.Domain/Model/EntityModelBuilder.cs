namespace Wdc.Domain;

/// <summary>Declares the fields of an entity.</summary>
public sealed class EntityModelBuilder<TEntity>
    where TEntity : class
{
    private readonly string _name;
    private readonly Func<TEntity> _factory;
    private readonly List<FieldModel<TEntity>> _fields = [];

    internal EntityModelBuilder(string name, Func<TEntity> factory)
    {
        _name = name;
        _factory = factory;
    }

    /// <summary>The key field (a scalar). Always part of the default projection.</summary>
    public EntityModelBuilder<TEntity> Key<T>(ValueKind<T> kind, string name, Func<TEntity, T> get, Action<TEntity, T> set) =>
        Add(new ScalarField<TEntity, T>(name, kind, get, set, isKey: true, inDefault: true));

    /// <summary>A scalar field.</summary>
    /// <remarks><c>inDefaultProjection: false</c> for what should only come when asked (a blob, a secret).</remarks>
    public EntityModelBuilder<TEntity> Field<T>(ValueKind<T> kind, string name, Func<TEntity, T> get,
        Action<TEntity, T> set, bool inDefaultProjection = true) =>
        Add(new ScalarField<TEntity, T>(name, kind, get, set, isKey: false, inDefaultProjection));

    /// <summary>A reference to another entity. <paramref name="model"/> is resolved lazily (models may refer to each other).</summary>
    public EntityModelBuilder<TEntity> Reference<TChild>(string name, Func<TEntity, TChild?> get, Action<TEntity, TChild?> set,
        Func<EntityModel<TChild>> model)
        where TChild : class =>
        Add(new ReferenceField<TEntity, TChild>(name, get, set, model));

    /// <summary>A list of other entities. <paramref name="newCriteria"/> lets a projection envelope carry a child filter.</summary>
    public EntityModelBuilder<TEntity> Collection<TChild>(string name, Func<TEntity, List<TChild>?> get,
        Action<TEntity, List<TChild>?> set, Func<EntityModel<TChild>> model, Func<ICriteria>? newCriteria = null)
        where TChild : class =>
        Add(new CollectionField<TEntity, TChild>(name, get, set, model, newCriteria));

    public EntityModel<TEntity> Build() => new(_name, _factory, [.. _fields]);

    private EntityModelBuilder<TEntity> Add(FieldModel<TEntity> field)
    {
        if (_fields.Any(f => f.Name == field.Name))
        {
            throw new ArgumentException($"{_name} declares '{field.Name}' twice.", nameof(field));
        }

        if (field.IsKey && _fields.Any(f => f.IsKey))
        {
            throw new ArgumentException($"{_name} declares two keys.", nameof(field));
        }

        _fields.Add(field);
        return this;
    }
}
