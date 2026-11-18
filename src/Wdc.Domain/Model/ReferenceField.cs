using System.Text.Json;

namespace Wdc.Domain;

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
