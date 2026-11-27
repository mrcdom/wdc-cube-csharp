using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>
/// What a repository's <c>Conditions</c> gets: the alias of the table being filtered, and the translation of each
/// criterion. Translating through here marks the criterion as covered; a criterion that came filled and that no
/// translation covered makes the statement fail as a configuration error instead of being silently ignored (a
/// restriction left out would widen what the caller reaches).
/// </summary>
public sealed class FilterContext
{
    private readonly SqlWriter _writer;
    private readonly HashSet<Criterion> _covered = new(ReferenceEqualityComparer.Instance);

    internal FilterContext(string alias, SqlWriter writer)
    {
        Alias = alias;
        _writer = writer;
    }

    /// <summary>The alias of the table being filtered.</summary>
    public string Alias { get; }

    /// <summary><c>alias.column</c>.</summary>
    public ISqlFragment Column(string column) => Sql.Column(Alias, column);

    /// <summary>An alias no other table of the statement uses (for an <c>EXISTS</c> on another table).</summary>
    public string NewAlias(string prefix) => _writer.NextAlias(prefix);

    /// <summary>The criterion on a column of this table; <see langword="null"/> when it asks nothing.</summary>
    public ISqlFragment? Translate(string column, Criterion criterion) => Translate(Column(column), criterion);

    /// <summary>The criterion on any expression (a column of another table, inside an <c>EXISTS</c>).</summary>
    public ISqlFragment? Translate(ISqlFragment expression, Criterion criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        _covered.Add(criterion);
        return Sql.Translate(expression, criterion);
    }

    /// <summary>
    /// The criterion on any of several columns of this table: the whole criterion on each column, the columns joined
    /// with OR (a term searched in the name or the description). <see langword="null"/> when it asks nothing or there
    /// are no columns.
    /// </summary>
    /// <exception cref="InvalidOperationException">The criterion carries a restriction: in an OR across columns it
    /// would stop restricting.</exception>
    public ISqlFragment? TranslateAny(IEnumerable<string> columns, Criterion criterion)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(criterion);
        if (criterion.IsRestricted)
        {
            throw new InvalidOperationException(
                $"'{criterion.Name}' carries a restriction: it cannot be translated over several columns joined by OR.");
        }

        _covered.Add(criterion);
        return Sql.Or(columns.Select(c => Sql.Translate(Column(c), criterion)));
    }

    /// <summary>
    /// The alternative groups of a criteria, each translated by <paramref name="translate"/> (the repository's own
    /// conditions), joined with OR. <see langword="null"/> when there are no groups, or when some group asks nothing:
    /// "or anything" matches every row.
    /// </summary>
    public ISqlFragment? AnyOf<TCriteria>(IReadOnlyList<TCriteria> groups, Func<TCriteria, ISqlFragment?> translate)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(translate);
        if (groups.Count == 0)
        {
            return null;
        }

        var alternatives = new List<ISqlFragment>(groups.Count);
        foreach (var group in groups)
        {
            if (translate(group) is not { } condition)
            {
                return null;
            }

            alternatives.Add(condition);
        }

        return Sql.Or(alternatives);
    }

    /// <summary>Whether the criterion asks for something (requests or restrictions): an <c>EXISTS</c> is only worth it then.</summary>
    public static bool Asks(Criterion criterion) => criterion is { IsSet: true } or { IsRestricted: true };

    internal IEnumerable<Criterion> Uncovered(ICriteria criteria) =>
        criteria.Criterions.Where(c => Asks(c) && !_covered.Contains(c));
}
