using System.Globalization;
using Wdc.Domain;

namespace Wdc.Persistence.PostgreSql;

/// <summary>
/// PostgreSQL. A row becomes a <c>jsonb</c> object built with <c>jsonb_build_object</c>, in blocks of at most 50 pairs
/// joined with <c>||</c> (the function takes at most 100 arguments); collections use <c>jsonb_agg</c> ordered inside the
/// aggregate.
/// </summary>
/// <remarks>
/// Store instants in <c>timestamptz</c>: Npgsql writes <see cref="DateTimeOffset"/> as <c>timestamptz</c>, and a
/// <c>timestamp</c> column would convert it with the session's time zone.
/// </remarks>
public sealed class PostgreSqlDialect : ISqlDialect
{
    private const int PairsPerBlock = 50;

    public static PostgreSqlDialect Instance { get; } = new();

    public string Name => "PostgreSQL";

    public string QuoteIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public void WriteJsonObject(SqlWriter writer, IReadOnlyList<JsonEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            writer.Append("'{}'::jsonb");
            return;
        }

        writer.Append('(');
        writer.Join(" || ", entries.Chunk(PairsPerBlock), (w, block) =>
        {
            w.Append("jsonb_build_object(");
            w.Join(", ", block, (ww, entry) =>
            {
                ww.Append(Literal(entry.Key)).Append(", ");
                if (entry.Kind?.ClrType == typeof(byte[]))
                {
                    ww.Append("encode(");
                    entry.Value(ww);
                    ww.Append(", 'base64')");
                }
                else
                {
                    entry.Value(ww);
                }
            });
            w.Append(')');
        });
        writer.Append(')');
    }

    // PostgreSQL accepts subqueries inside the aggregate: order it in place.
    public void WriteJsonArraySubquery(SqlWriter writer, Action<SqlWriter> element,
        IReadOnlyList<(Action<SqlWriter> Expression, bool Descending)> orderBy, Action<SqlWriter> fromWhere)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(orderBy);
        ArgumentNullException.ThrowIfNull(fromWhere);
        writer.Append("(SELECT coalesce(jsonb_agg(");
        element(writer);
        if (orderBy.Count > 0)
        {
            writer.Append(" ORDER BY ").Join(", ", orderBy, (w, o) =>
            {
                o.Expression(w);
                w.Append(o.Descending ? " DESC" : " ASC");
            });
        }

        writer.Append("), '[]'::jsonb)");
        fromWhere(writer);
        writer.Append(')');
    }

    public void WriteJsonAsText(SqlWriter writer, Action<SqlWriter> json)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(json);
        writer.Append('(');
        json(writer);
        writer.Append(")::text");
    }

    public void WriteKeyObject(SqlWriter writer, string key, Action<SqlWriter> column, IValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(column);
        writer.Append("CASE WHEN ");
        column(writer);
        writer.Append(" IS NULL THEN NULL ELSE jsonb_build_object(").Append(Literal(key)).Append(", ");
        column(writer);
        writer.Append(") END");
    }

    public void WriteBoolean(SqlWriter writer, bool value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Append(value ? "TRUE" : "FALSE");
    }

    public void WriteLike(SqlWriter writer, ISqlFragment expr, string pattern, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(expr);
        expr.Render(writer);
        writer.Append(ignoreCase ? " ILIKE " : " LIKE ").Parameter(pattern).Append(" ESCAPE '\\'");
    }

    public void WriteSlice(SqlWriter writer, int? limit, int? offset)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (limit is { } l)
        {
            writer.Append(" LIMIT ").Append(l.ToString(CultureInfo.InvariantCulture));
        }

        if (offset is { } o)
        {
            writer.Append(" OFFSET ").Append(o.ToString(CultureInfo.InvariantCulture));
        }
    }

    public void WriteDeleteFrom(SqlWriter writer, string table, string alias)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Append("DELETE FROM ").Table(table).Append(" AS ").Identifier(alias);
    }

    public void WriteInsertReturningKey(SqlWriter writer, string table, IReadOnlyList<(string Column, object? Value)> values,
        string keyColumn)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(values);
        writer.Append("INSERT INTO ").Table(table);
        if (values.Count == 0)
        {
            writer.Append(" DEFAULT VALUES");
        }
        else
        {
            writer.Append(" (").Join(", ", values, (w, v) => w.Identifier(v.Column)).Append(") VALUES (")
                .Join(", ", values, (w, v) => w.Parameter(v.Value)).Append(')');
        }

        writer.Append(" RETURNING ").Identifier(keyColumn);
    }

    private static string Literal(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
}
