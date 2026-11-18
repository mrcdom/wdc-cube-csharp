using System.Text;

namespace Wdc.Persistence;

/// <summary>
/// Builds a statement. Values always go as parameters, never into the text. Aliases are numbered per statement, so
/// nested subqueries never collide.
/// </summary>
public sealed class SqlWriter
{
    private readonly StringBuilder _text = new();
    private readonly List<object?> _parameters = [];
    private int _aliases;

    public SqlWriter(ISqlDialect dialect, string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        Dialect = dialect;
        Schema = schema;
    }

    public ISqlDialect Dialect { get; }

    /// <summary>The schema tables are qualified with, if any.</summary>
    public string? Schema { get; }

    public SqlWriter Append(string text)
    {
        _text.Append(text);
        return this;
    }

    public SqlWriter Append(char ch)
    {
        _text.Append(ch);
        return this;
    }

    public SqlWriter Identifier(string name) => Append(Dialect.QuoteIdentifier(name));

    /// <summary>A table, qualified with the schema.</summary>
    public SqlWriter Table(string name)
    {
        if (Schema is not null)
        {
            Identifier(Schema).Append('.');
        }

        return Identifier(name);
    }

    /// <summary><c>alias.column</c>.</summary>
    public SqlWriter Column(string alias, string column) => Identifier(alias).Append('.').Identifier(column);

    /// <summary>A parameter placeholder for <paramref name="value"/> (already in its database form).</summary>
    public SqlWriter Parameter(object? value)
    {
        _parameters.Add(value);
        return Append("@p").Append((_parameters.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A fresh alias: <c>prefix1</c>, <c>prefix2</c>...</summary>
    public string NextAlias(string prefix) =>
        prefix + (++_aliases).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Writes <paramref name="items"/> separated by <paramref name="separator"/>.</summary>
    public SqlWriter Join<T>(string separator, IEnumerable<T> items, Action<SqlWriter, T> write)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(write);
        var first = true;
        foreach (var item in items)
        {
            if (!first)
            {
                Append(separator);
            }

            write(this, item);
            first = false;
        }

        return this;
    }

    public SqlStatement ToStatement() => new(_text.ToString(), [.. _parameters]);

    public override string ToString() => _text.ToString();
}
