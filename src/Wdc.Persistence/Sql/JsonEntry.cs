using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>One entry of a JSON object built by the database.</summary>
/// <param name="Key">The JSON key (the field name).</param>
/// <param name="Value">Writes the value expression.</param>
/// <param name="Kind">The scalar's kind, or <see langword="null"/> when the value is already JSON (a nested object or array).</param>
public sealed record JsonEntry(string Key, Action<SqlWriter> Value, IValueKind? Kind);
