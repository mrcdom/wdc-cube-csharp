using System.Text.Json;

namespace Wdc.Domain;

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
