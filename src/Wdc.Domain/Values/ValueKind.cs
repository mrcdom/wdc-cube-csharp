using System.Text.Json;

namespace Wdc.Domain;

/// <summary>
/// Everything the framework needs to know about one kind of value, declared once and used by entity models,
/// criteria, JSON codecs and the SQL layer: how it travels in JSON, which marker means "wanted" in a projection, and
/// how it goes to and comes from the database.
/// </summary>
/// <typeparam name="T">The nullable CLR type (<c>long?</c>, <c>string?</c>, <c>DateTimeOffset?</c>...).</typeparam>
public abstract class ValueKind<T> : IValueKind
{
    /// <summary>A short name, for messages.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// The value a projection bean holds to mean "this field is wanted". Only its presence matters.
    /// </summary>
    public abstract T Marker { get; }

    /// <summary>Whether <paramref name="value"/> is absent.</summary>
    public static bool IsNull(T value) => value is null;

    /// <summary>Writes a non-null value.</summary>
    public abstract void Write(Utf8JsonWriter writer, T value);

    /// <summary>
    /// Reads the value at the reader's current token. JSON <c>null</c> reads as absent; a value of the wrong shape
    /// is tolerated when it converts (a number sent as text), and otherwise reads as absent.
    /// </summary>
    public abstract T Read(ref Utf8JsonReader reader);

    /// <summary>The value as a database parameter.</summary>
    public virtual object? ToDatabase(T value) => value;

    /// <summary>The value from a database column (or a JSON value already parsed by the database).</summary>
    public abstract T FromDatabase(object? value);

    object? IValueKind.ToDatabaseObject(object? value) => value is null ? null : ToDatabase((T)value);

    object? IValueKind.FromDatabaseObject(object? value) => FromDatabase(value);

    Type IValueKind.ClrType => typeof(T);

    void IValueKind.WriteObject(Utf8JsonWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            Write(writer, (T)value);
        }
    }
}
