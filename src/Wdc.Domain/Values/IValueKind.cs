using System.Text.Json;

namespace Wdc.Domain;

/// <summary>The untyped view of a <see cref="ValueKind{T}"/>, for code that only moves values around.</summary>
public interface IValueKind
{
    string Name { get; }

    Type ClrType { get; }

    /// <summary>The names an enum kind travels as, in the order of their values; <see langword="null"/> for any other kind.</summary>
    IReadOnlyList<string>? Choices { get; }

    object? ToDatabaseObject(object? value);

    object? FromDatabaseObject(object? value);

    /// <summary>Writes a value of this kind (or null) as a JSON value.</summary>
    void WriteObject(Utf8JsonWriter writer, object? value);
}
