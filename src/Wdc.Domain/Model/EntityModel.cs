using System.Text.Json;

namespace Wdc.Domain;

/// <summary>What a field holds.</summary>
public enum FieldKind
{
    /// <summary>A single value (number, text, date...).</summary>
    Scalar,

    /// <summary>Another entity (N:1 or 1:1).</summary>
    Reference,

    /// <summary>A list of other entities (1:N).</summary>
    Collection,
}

/// <summary>The untyped view of an entity model.</summary>
public interface IEntityModel
{
    Type EntityType { get; }

    string Name { get; }
}

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

/// <summary>One field of an entity model.</summary>
public abstract class FieldModel<TEntity>
    where TEntity : class
{
    protected FieldModel(string name, FieldKind kind, bool isKey, bool inDefault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Kind = kind;
        IsKey = isKey;
        InDefaultProjection = inDefault;
    }

    /// <summary>The name in JSON (and, by default, how the SQL layer refers to it).</summary>
    public string Name { get; }

    public FieldKind Kind { get; }

    public bool IsKey { get; }

    public bool InDefaultProjection { get; }

    /// <summary>Whether the field holds a value (in a result) or is marked (in a projection).</summary>
    public abstract bool HasValue(TEntity entity);

    /// <summary>The value, boxed.</summary>
    public abstract object? GetValue(TEntity entity);

    /// <summary>Sets the value, boxed (a child entity or list for references and collections).</summary>
    public abstract void SetValue(TEntity entity, object? value);

    /// <summary>Copies this field's value from <paramref name="source"/> to <paramref name="target"/>.</summary>
    public void Copy(TEntity source, TEntity target) => SetValue(target, GetValue(source));

    /// <summary>Marks the field in a projection: a marker, a key-only child, or a list holding the child's own fields.</summary>
    public abstract void MarkOwnFields(TEntity projection);

    /// <summary>Whether this field differs between two beans (references compare by key; collections are not compared).</summary>
    public abstract bool Differs(TEntity next, TEntity previous);

    internal abstract void WriteJson(Utf8JsonWriter writer, TEntity entity, TEntity? projection, HashSet<object> visited);

    internal abstract void ReadJson(ref Utf8JsonReader reader, TEntity entity, TEntity? presence);
}

/// <summary>A single value.</summary>
public sealed class ScalarField<TEntity, T> : FieldModel<TEntity>
    where TEntity : class
{
    internal ScalarField(string name, ValueKind<T> kind, Func<TEntity, T> get, Action<TEntity, T> set, bool isKey, bool inDefault)
        : base(name, FieldKind.Scalar, isKey, inDefault)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        ValueKind = kind;
        Get = get;
        Set = set;
    }

    public ValueKind<T> ValueKind { get; }

    public Func<TEntity, T> Get { get; }

    public Action<TEntity, T> Set { get; }

    public override bool HasValue(TEntity entity) => !ValueKind<T>.IsNull(Get(entity));

    public override object? GetValue(TEntity entity) => Get(entity);

    public override void SetValue(TEntity entity, object? value) => Set(entity, value is null ? default! : (T)value);

    public override void MarkOwnFields(TEntity projection) => Set(projection, ValueKind.Marker);

    public override bool Differs(TEntity next, TEntity previous) => !Same(Get(next), Get(previous));

    internal override void WriteJson(Utf8JsonWriter writer, TEntity entity, TEntity? projection, HashSet<object> visited)
    {
        writer.WritePropertyName(Name);
        var value = Get(entity);
        if (ValueKind<T>.IsNull(value))
        {
            writer.WriteNullValue();
        }
        else
        {
            ValueKind.Write(writer, value);
        }
    }

    internal override void ReadJson(ref Utf8JsonReader reader, TEntity entity, TEntity? presence)
    {
        Set(entity, reader.TokenType == JsonTokenType.Null ? default! : ValueKind.Read(ref reader));
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
        }

        if (presence is not null)
        {
            MarkOwnFields(presence); // present, even as an explicit null: "set this field"
        }
    }

    private static bool Same(T a, T b) =>
        a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : EqualityComparer<T>.Default.Equals(a, b);
}

/// <summary>Another entity (N:1, 1:1).</summary>
public sealed class ReferenceField<TEntity, TChild> : FieldModel<TEntity>
    where TEntity : class
    where TChild : class
{
    private readonly Lazy<EntityModel<TChild>> _model;

    internal ReferenceField(string name, Func<TEntity, TChild?> get, Action<TEntity, TChild?> set, Func<EntityModel<TChild>> model)
        : base(name, FieldKind.Reference, isKey: false, inDefault: true)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(model);
        Get = get;
        Set = set;
        _model = new Lazy<EntityModel<TChild>>(model);
    }

    public EntityModel<TChild> Model => _model.Value;

    public Func<TEntity, TChild?> Get { get; }

    public Action<TEntity, TChild?> Set { get; }

    public override bool HasValue(TEntity entity) => Get(entity) is not null;

    public override object? GetValue(TEntity entity) => Get(entity);

    public override void SetValue(TEntity entity, object? value) => Set(entity, (TChild?)value);

    public override void MarkOwnFields(TEntity projection) => Set(projection, Model.KeyProjection());

    public override bool Differs(TEntity next, TEntity previous)
    {
        var a = Get(next);
        var b = Get(previous);
        return a is null || b is null ? !ReferenceEquals(a, b) : !Equals(Model.KeyOf(a), Model.KeyOf(b));
    }

    internal override void WriteJson(Utf8JsonWriter writer, TEntity entity, TEntity? projection, HashSet<object> visited)
    {
        writer.WritePropertyName(Name);
        var child = Get(entity);
        if (child is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            EntityJson.WriteObject(writer, Model, child, projection is null ? null : Get(projection), visited);
        }
    }

    internal override void ReadJson(ref Utf8JsonReader reader, TEntity entity, TEntity? presence)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            Set(entity, null);
            if (presence is not null)
            {
                Set(presence, Model.KeyProjection());
            }

            return;
        }

        var childPresence = presence is null ? null : Model.New();
        Set(entity, EntityJson.ReadObject(ref reader, Model, childPresence));
        if (presence is not null)
        {
            Set(presence, childPresence);
        }
    }
}

/// <summary>A list of other entities (1:N).</summary>
public sealed class CollectionField<TEntity, TChild> : FieldModel<TEntity>
    where TEntity : class
    where TChild : class
{
    private readonly Lazy<EntityModel<TChild>> _model;

    internal CollectionField(string name, Func<TEntity, List<TChild>?> get, Action<TEntity, List<TChild>?> set,
        Func<EntityModel<TChild>> model, Func<ICriteria>? newCriteria)
        : base(name, FieldKind.Collection, isKey: false, inDefault: false)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(model);
        Get = get;
        Set = set;
        _model = new Lazy<EntityModel<TChild>>(model);
        NewCriteria = newCriteria;
    }

    /// <summary>Creates the child's criteria, to read a projection envelope that filters the collection.</summary>
    public Func<ICriteria>? NewCriteria { get; }

    public EntityModel<TChild> Model => _model.Value;

    public Func<TEntity, List<TChild>?> Get { get; }

    public Action<TEntity, List<TChild>?> Set { get; }

    public override bool HasValue(TEntity entity) => Get(entity) is not null;

    public override object? GetValue(TEntity entity) => Get(entity);

    public override void SetValue(TEntity entity, object? value) => Set(entity, (List<TChild>?)value);

    public override void MarkOwnFields(TEntity projection) =>
        Set(projection, new ProjectionList<TChild>(Model.OwnFieldsProjection()));

    public override bool Differs(TEntity next, TEntity previous) => false;

    internal override void WriteJson(Utf8JsonWriter writer, TEntity entity, TEntity? projection, HashSet<object> visited)
    {
        writer.WritePropertyName(Name);
        var list = Get(entity);
        if (list is null)
        {
            writer.WriteNullValue();
            return;
        }

        // A projection list that filters or slices travels as an envelope: { shape, where, limit, offset }.
        if (list is ProjectionList<TChild> { } p && (p.Criteria is not null || p.Limit is not null || p.Offset is not null))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("shape");
            EntityJson.WriteObject(writer, Model, p.Shape, null, visited);
            if (p.Criteria is ICriteria criteria)
            {
                writer.WritePropertyName("where");
                CriteriaCodec.Write(writer, criteria);
            }

            if (p.Limit is { } limit)
            {
                writer.WriteNumber("limit", limit);
            }

            if (p.Offset is { } offset)
            {
                writer.WriteNumber("offset", offset);
            }

            writer.WriteEndObject();
            return;
        }

        var shape = projection is not null && Get(projection) is { Count: > 0 } shapes ? shapes[0] : null;
        writer.WriteStartArray();
        foreach (var item in list)
        {
            EntityJson.WriteObject(writer, Model, item, shape, visited);
        }

        writer.WriteEndArray();
    }

    internal override void ReadJson(ref Utf8JsonReader reader, TEntity entity, TEntity? presence)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartArray:
                var items = new List<TChild>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        items.Add(EntityJson.ReadObject(ref reader, Model, null));
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                Set(entity, items);
                break;
            case JsonTokenType.StartObject:
                Set(entity, ReadEnvelope(ref reader));
                break;
            default:
                reader.Skip();
                Set(entity, null);
                break;
        }

        if (presence is not null)
        {
            MarkOwnFields(presence);
        }
    }

    private ProjectionList<TChild> ReadEnvelope(ref Utf8JsonReader reader)
    {
        TChild? shape = null;
        ICriteria? criteria = null;
        int? limit = null;
        int? offset = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();
            switch (property)
            {
                case "shape" when reader.TokenType == JsonTokenType.StartObject:
                    shape = EntityJson.ReadObject(ref reader, Model, null);
                    break;
                case "where" when reader.TokenType == JsonTokenType.StartObject && NewCriteria is not null:
                    criteria = NewCriteria();
                    CriteriaCodec.Read(ref reader, criteria);
                    break;
                case "limit" when reader.TokenType == JsonTokenType.Number:
                    limit = reader.GetInt32();
                    break;
                case "offset" when reader.TokenType == JsonTokenType.Number:
                    offset = reader.GetInt32();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new ProjectionList<TChild>(shape ?? Model.OwnFieldsProjection(), criteria, limit, offset);
    }
}
