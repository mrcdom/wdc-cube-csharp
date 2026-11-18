namespace Wdc.Persistence;

/// <summary>What a custom filter gets: the alias of the table being filtered.</summary>
public sealed class FilterContext(string alias)
{
    public string Alias { get; } = alias;

    /// <summary><c>alias.column</c>.</summary>
    public ISqlFragment Column(string column) => Sql.Column(Alias, column);
}
