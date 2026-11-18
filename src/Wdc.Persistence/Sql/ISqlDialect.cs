using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>One entry of a JSON object built by the database.</summary>
/// <param name="Key">The JSON key (the field name).</param>
/// <param name="Value">Writes the value expression.</param>
/// <param name="Kind">The scalar's kind, or <see langword="null"/> when the value is already JSON (a nested object or array).</param>
public sealed record JsonEntry(string Key, Action<SqlWriter> Value, IValueKind? Kind);

/// <summary>
/// What changes from one database to another: identifier quoting, the JSON functions that build a row as one JSON
/// object, LIKE, booleans, slicing, and the insert that returns the generated key.
/// </summary>
public interface ISqlDialect
{
    string Name { get; }

    string QuoteIdentifier(string name);

    /// <summary>A JSON object (as the dialect's JSON value) with these entries; null scalars become JSON null.</summary>
    void WriteJsonObject(SqlWriter writer, IReadOnlyList<JsonEntry> entries);

    /// <summary>
    /// A scalar subquery giving the JSON array of the rows that <paramref name="fromWhere"/> selects (it writes
    /// <c>FROM ... WHERE ...</c>), each row shaped by <paramref name="element"/>, ordered by <paramref name="orderBy"/>;
    /// an empty set gives <c>[]</c>.
    /// </summary>
    void WriteJsonArraySubquery(SqlWriter writer, Action<SqlWriter> element, IReadOnlyList<(Action<SqlWriter> Expression, bool Descending)> orderBy,
        Action<SqlWriter> fromWhere);

    /// <summary>Turns the dialect's JSON value into text, for the outermost select.</summary>
    void WriteJsonAsText(SqlWriter writer, Action<SqlWriter> json);

    /// <summary>A JSON object built from foreign-key columns (a reference asked only for its key), null when the key is null.</summary>
    void WriteKeyObject(SqlWriter writer, string key, Action<SqlWriter> column, IValueKind kind);

    void WriteBoolean(SqlWriter writer, bool value);

    /// <summary>LIKE with <c>\</c> as escape.</summary>
    void WriteLike(SqlWriter writer, ISqlFragment expr, string pattern, bool ignoreCase);

    /// <summary>LIMIT/OFFSET, after the ORDER BY (which the caller always writes when slicing).</summary>
    void WriteSlice(SqlWriter writer, int? limit, int? offset);

    /// <summary>The DELETE head: <c>DELETE FROM t AS a</c> or <c>DELETE a FROM t AS a</c>.</summary>
    void WriteDeleteFrom(SqlWriter writer, string table, string alias);

    /// <summary>
    /// <c>INSERT INTO t (cols) VALUES (...)</c> returning the key column as a single value.
    /// </summary>
    void WriteInsertReturningKey(SqlWriter writer, string table, IReadOnlyList<(string Column, object? Value)> values,
        string keyColumn);
}
