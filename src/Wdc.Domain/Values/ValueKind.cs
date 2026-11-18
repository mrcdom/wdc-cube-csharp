using System.Globalization;
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
}

/// <summary>The untyped view of a <see cref="ValueKind{T}"/>, for code that only moves values around.</summary>
public interface IValueKind
{
    string Name { get; }

    Type ClrType { get; }

    object? ToDatabaseObject(object? value);

    object? FromDatabaseObject(object? value);
}

/// <summary>The value kinds the framework knows.</summary>
public static class ValueKinds
{
    public static ValueKind<int?> Int32 { get; } = new Int32Kind();

    public static ValueKind<long?> Int64 { get; } = new Int64Kind();

    public static ValueKind<double?> Double { get; } = new DoubleKind();

    public static ValueKind<decimal?> Decimal { get; } = new DecimalKind();

    public static ValueKind<string?> String { get; } = new StringKind();

    public static ValueKind<bool?> Boolean { get; } = new BooleanKind();

    /// <summary>An instant. Travels as ISO-8601 in UTC (<c>2026-11-18T12:00:00Z</c>); reads offsets too.</summary>
    public static ValueKind<DateTimeOffset?> Instant { get; } = new InstantKind();

    /// <summary>A calendar date (<c>2026-11-18</c>).</summary>
    public static ValueKind<DateOnly?> Date { get; } = new DateKind();

    public static ValueKind<Guid?> Uuid { get; } = new UuidKind();

    /// <summary>Bytes; travel as Base64 (line breaks tolerated).</summary>
    public static ValueKind<byte[]?> Bytes { get; } = new BytesKind();

    /// <summary>An enum, travelling by its name.</summary>
    public static ValueKind<TEnum?> Enum<TEnum>()
        where TEnum : struct, System.Enum => EnumKind<TEnum>.Instance;

    private static string? ReadText(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.HasValueSequence
            ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence)
            : reader.ValueSpan),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        _ => null,
    };

    private sealed class Int32Kind : ValueKind<int?>
    {
        public override string Name => "int32";

        public override int? Marker => 1;

        public override void Write(Utf8JsonWriter writer, int? value) => writer.WriteNumberValue(value!.Value);

        public override int? Read(ref Utf8JsonReader reader) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var v) ? v
            : int.TryParse(ReadText(ref reader), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : null;

        public override int? FromDatabase(object? value) => value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private sealed class Int64Kind : ValueKind<long?>
    {
        public override string Name => "int64";

        public override long? Marker => 1L;

        public override void Write(Utf8JsonWriter writer, long? value) => writer.WriteNumberValue(value!.Value);

        public override long? Read(ref Utf8JsonReader reader) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var v) ? v
            : long.TryParse(ReadText(ref reader), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : null;

        public override long? FromDatabase(object? value) => value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private sealed class DoubleKind : ValueKind<double?>
    {
        public override string Name => "double";

        public override double? Marker => 1d;

        public override void Write(Utf8JsonWriter writer, double? value) => writer.WriteNumberValue(value!.Value);

        public override double? Read(ref Utf8JsonReader reader) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var v) ? v
            : double.TryParse(ReadText(ref reader), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null;

        public override double? FromDatabase(object? value) => value is null or DBNull ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private sealed class DecimalKind : ValueKind<decimal?>
    {
        public override string Name => "decimal";

        public override decimal? Marker => 1m;

        public override void Write(Utf8JsonWriter writer, decimal? value) => writer.WriteNumberValue(value!.Value);

        public override decimal? Read(ref Utf8JsonReader reader) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var v) ? v
            : decimal.TryParse(ReadText(ref reader), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null;

        public override decimal? FromDatabase(object? value) => value is null or DBNull ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private sealed class StringKind : ValueKind<string?>
    {
        public override string Name => "string";

        public override string? Marker => "~";

        public override void Write(Utf8JsonWriter writer, string? value) => writer.WriteStringValue(value);

        public override string? Read(ref Utf8JsonReader reader) => ReadText(ref reader);

        public override string? FromDatabase(object? value) => value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private sealed class BooleanKind : ValueKind<bool?>
    {
        public override string Name => "boolean";

        public override bool? Marker => true;

        public override void Write(Utf8JsonWriter writer, bool? value) => writer.WriteBooleanValue(value!.Value);

        public override bool? Read(ref Utf8JsonReader reader) => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.TryGetInt32(out var n) ? n != 0 : null, // a bit column
            _ => bool.TryParse(ReadText(ref reader), out var t) ? t : null,
        };

        public override bool? FromDatabase(object? value) => value switch
        {
            null or DBNull => null,
            bool b => b,
            string s => bool.TryParse(s, out var t) ? t : s == "1",
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
        };
    }

    private sealed class InstantKind : ValueKind<DateTimeOffset?>
    {
        public override string Name => "instant";

        public override DateTimeOffset? Marker => DateTimeOffset.UnixEpoch;

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value) =>
            writer.WriteStringValue(Format(value!.Value));

        public override DateTimeOffset? Read(ref Utf8JsonReader reader) => Parse(ReadText(ref reader));

        /// <summary>Columns without a zone hold UTC by convention.</summary>
        public override object? ToDatabase(DateTimeOffset? value) => value?.ToUniversalTime();

        public override DateTimeOffset? FromDatabase(object? value) => value switch
        {
            null or DBNull => null,
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s => Parse(s),
            _ => throw new InvalidCastException($"Cannot read an instant from {value.GetType().Name}."),
        };

        internal static string Format(DateTimeOffset value) =>
            value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

        // Accepts an offset, 'Z', or none (UTC by convention), and up to 7 fractional digits.
        private static DateTimeOffset? Parse(string? text) =>
            text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var v)
                ? v
                : null;
    }

    private sealed class DateKind : ValueKind<DateOnly?>
    {
        public override string Name => "date";

        public override DateOnly? Marker => new DateOnly(1970, 1, 1);

        public override void Write(Utf8JsonWriter writer, DateOnly? value) =>
            writer.WriteStringValue(value!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        public override DateOnly? Read(ref Utf8JsonReader reader) =>
            DateOnly.TryParseExact(ReadText(ref reader), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)
                ? v
                : null;

        public override DateOnly? FromDatabase(object? value) => value switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : null,
            _ => throw new InvalidCastException($"Cannot read a date from {value.GetType().Name}."),
        };
    }

    private sealed class UuidKind : ValueKind<Guid?>
    {
        public override string Name => "uuid";

        public override Guid? Marker => Guid.Empty;

        public override void Write(Utf8JsonWriter writer, Guid? value) => writer.WriteStringValue(value!.Value);

        public override Guid? Read(ref Utf8JsonReader reader) => Guid.TryParse(ReadText(ref reader), out var v) ? v : null;

        public override Guid? FromDatabase(object? value) => value switch
        {
            null or DBNull => null,
            Guid g => g,
            string s => Guid.TryParse(s, out var v) ? v : null,
            _ => throw new InvalidCastException($"Cannot read a uuid from {value.GetType().Name}."),
        };
    }

    private sealed class BytesKind : ValueKind<byte[]?>
    {
        public override string Name => "bytes";

        public override byte[]? Marker => [];

        public override void Write(Utf8JsonWriter writer, byte[]? value) => writer.WriteBase64StringValue(value);

        public override byte[]? Read(ref Utf8JsonReader reader) => FromBase64(ReadText(ref reader));

        public override byte[]? FromDatabase(object? value) => value switch
        {
            null or DBNull => null,
            byte[] b => b,
            string s => FromBase64(s),
            _ => throw new InvalidCastException($"Cannot read bytes from {value.GetType().Name}."),
        };

        // PostgreSQL's encode(..., 'base64') breaks lines every 76 characters.
        private static byte[]? FromBase64(string? text)
        {
            if (text is null)
            {
                return null;
            }

            try
            {
                return Convert.FromBase64String(text.Replace("\n", string.Empty, StringComparison.Ordinal)
                    .Replace("\r", string.Empty, StringComparison.Ordinal));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    private sealed class EnumKind<TEnum> : ValueKind<TEnum?>
        where TEnum : struct, System.Enum
    {
        public static readonly EnumKind<TEnum> Instance = new();

        public override string Name => typeof(TEnum).Name;

        public override TEnum? Marker => default(TEnum);

        public override void Write(Utf8JsonWriter writer, TEnum? value) => writer.WriteStringValue(value!.Value.ToString());

        public override TEnum? Read(ref Utf8JsonReader reader) => Parse(ReadText(ref reader));

        public override object? ToDatabase(TEnum? value) => value?.ToString();

        public override TEnum? FromDatabase(object? value) => value is null or DBNull ? null : Parse(Convert.ToString(value, CultureInfo.InvariantCulture));

        private static TEnum? Parse(string? text) =>
            text is not null && System.Enum.TryParse<TEnum>(text, ignoreCase: false, out var v) && System.Enum.IsDefined(v)
                ? v
                : null;
    }
}
