using System.Globalization;
using Wdc.Domain;

namespace Wdc.Persistence.SqlServer;

/// <summary>
/// SQL Server 2025. A row becomes <c>JSON_OBJECT</c>; collections use <c>JSON_ARRAYAGG ... ORDER BY</c>. A value that is
/// already JSON but comes from a subquery or an expression is wrapped in <c>JSON_QUERY</c>, otherwise it would be
/// embedded as an escaped string.
/// </summary>
/// <remarks>Store instants in <c>datetimeoffset</c>; bytes travel as Base64 (<c>BASE64_ENCODE</c>).</remarks>
public sealed class SqlServerDialect : ISqlDialect
{
    public static SqlServerDialect Instance { get; } = new();

    public string Name => "SQL Server";

    public string QuoteIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public void WriteJsonObject(SqlWriter writer, IReadOnlyList<JsonEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);
        writer.Append("JSON_OBJECT(");
        writer.Join(", ", entries, (w, entry) =>
        {
            w.Append(Literal(entry.Key)).Append(": ");
            if (entry.Kind is null)
            {
                w.Append("JSON_QUERY(");
                entry.Value(w);
                w.Append(')');
            }
            else if (entry.Kind.ClrType == typeof(byte[]))
            {
                w.Append("BASE64_ENCODE(");
                entry.Value(w);
                w.Append(')');
            }
            else
            {
                entry.Value(w);
            }
        });
        writer.Append(" NULL ON NULL)");
    }

    // SQL Server refuses a subquery inside an aggregate's argument, and a row's JSON may hold subqueries (its
    // references). So each row's JSON is built in a derived table, with its sort keys, and aggregated outside.
    public void WriteJsonArraySubquery(SqlWriter writer, Action<SqlWriter> element,
        IReadOnlyList<(Action<SqlWriter> Expression, bool Descending)> orderBy, Action<SqlWriter> fromWhere)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(orderBy);
        ArgumentNullException.ThrowIfNull(fromWhere);
        var rows = writer.NextAlias("j");
        writer.Append("(SELECT ISNULL(JSON_ARRAYAGG(JSON_QUERY(").Identifier(rows).Append(".[j])");
        if (orderBy.Count > 0)
        {
            writer.Append(" ORDER BY ");
            for (var i = 0; i < orderBy.Count; i++)
            {
                writer.Append(i == 0 ? string.Empty : ", ").Identifier(rows).Append(".[o")
                    .Append(i.ToString(CultureInfo.InvariantCulture)).Append(orderBy[i].Descending ? "] DESC" : "] ASC");
            }
        }

        writer.Append("), N'[]') FROM (SELECT ");
        element(writer);
        writer.Append(" AS [j]");
        for (var i = 0; i < orderBy.Count; i++)
        {
            writer.Append(", ");
            orderBy[i].Expression(writer);
            writer.Append(" AS [o").Append(i.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        fromWhere(writer);
        writer.Append(") ").Identifier(rows).Append(')');
    }

    public void WriteJsonAsText(SqlWriter writer, Action<SqlWriter> json)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(json);
        json(writer); // JSON_OBJECT already returns text
    }

    public void WriteKeyObject(SqlWriter writer, string key, Action<SqlWriter> column, IValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(column);
        writer.Append("CASE WHEN ");
        column(writer);
        writer.Append(" IS NULL THEN NULL ELSE JSON_OBJECT(").Append(Literal(key)).Append(": ");
        column(writer);
        writer.Append(") END");
    }

    public void WriteBoolean(SqlWriter writer, bool value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Append(value ? "1 = 1" : "1 = 0");
    }

    public void WriteLike(SqlWriter writer, ISqlFragment expr, string pattern, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(expr);
        if (ignoreCase)
        {
            // Independent of the column's collation.
            writer.Append("LOWER(");
            expr.Render(writer);
            writer.Append(") LIKE LOWER(").Parameter(pattern).Append(") ESCAPE '\\'");
        }
        else
        {
            expr.Render(writer);
            writer.Append(" LIKE ").Parameter(pattern).Append(" ESCAPE '\\'");
        }
    }

    public void WriteSlice(SqlWriter writer, int? limit, int? offset)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Append(" OFFSET ").Append((offset ?? 0).ToString(CultureInfo.InvariantCulture)).Append(" ROWS");
        if (limit is { } l)
        {
            writer.Append(" FETCH NEXT ").Append(l.ToString(CultureInfo.InvariantCulture)).Append(" ROWS ONLY");
        }
    }

    public void WriteDeleteFrom(SqlWriter writer, string table, string alias)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Append("DELETE ").Identifier(alias).Append(" FROM ").Table(table).Append(" AS ").Identifier(alias);
    }

    public void WriteInsertReturningKey(SqlWriter writer, string table, IReadOnlyList<(string Column, object? Value)> values,
        string keyColumn)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(values);
        writer.Append("INSERT INTO ").Table(table);
        if (values.Count > 0)
        {
            writer.Append(" (").Join(", ", values, (w, v) => w.Identifier(v.Column)).Append(')');
        }

        writer.Append(" OUTPUT INSERTED.").Identifier(keyColumn);
        if (values.Count == 0)
        {
            writer.Append(" DEFAULT VALUES");
        }
        else
        {
            writer.Append(" VALUES (").Join(", ", values, (w, v) => w.Parameter(v.Value)).Append(')');
        }
    }

    private static string Literal(string text) => "N'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
}
