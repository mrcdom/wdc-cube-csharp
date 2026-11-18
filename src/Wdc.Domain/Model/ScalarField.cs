using System.Text.Json;

namespace Wdc.Domain;

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

    public override IValueKind? ScalarKind => ValueKind;

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
