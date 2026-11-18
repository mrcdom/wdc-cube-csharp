using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>Something that renders itself into a statement.</summary>
public interface ISqlFragment
{
    void Render(SqlWriter writer);
}

/// <summary>Building blocks of WHERE clauses. Values are converted to their database form by the caller.</summary>
public static class Sql
{
    public static ISqlFragment Column(string alias, string column) => new Fragment(w => w.Column(alias, column));

    public static ISqlFragment Raw(Action<SqlWriter> render) => new Fragment(render);

    public static ISqlFragment True { get; } = new Fragment(w => w.Dialect.WriteBoolean(w, true));

    public static ISqlFragment False { get; } = new Fragment(w => w.Dialect.WriteBoolean(w, false));

    public static ISqlFragment Compare(ISqlFragment left, string op, object? value) =>
        new Fragment(w =>
        {
            left.Render(w);
            w.Append(' ').Append(op).Append(' ').Parameter(value);
        });

    public static ISqlFragment Equal(ISqlFragment left, ISqlFragment right) =>
        new Fragment(w =>
        {
            left.Render(w);
            w.Append(" = ");
            right.Render(w);
        });

    public static ISqlFragment IsNull(ISqlFragment expr) => new Fragment(w =>
    {
        expr.Render(w);
        w.Append(" IS NULL");
    });

    public static ISqlFragment IsNotNull(ISqlFragment expr) => new Fragment(w =>
    {
        expr.Render(w);
        w.Append(" IS NOT NULL");
    });

    public static ISqlFragment Between(ISqlFragment expr, object? start, object? end) => new Fragment(w =>
    {
        expr.Render(w);
        w.Append(" BETWEEN ").Parameter(start).Append(" AND ").Parameter(end);
    });

    /// <summary>IN; an empty list matches nothing; a null among the values also matches NULL.</summary>
    public static ISqlFragment In(ISqlFragment expr, IReadOnlyList<object?> values)
    {
        var present = values.Where(v => v is not null).ToList();
        var withNull = present.Count < values.Count;
        if (present.Count == 0)
        {
            return withNull ? IsNull(expr) : False;
        }

        var inList = new Fragment(w =>
        {
            expr.Render(w);
            w.Append(" IN (").Join(", ", present, (ww, v) => ww.Parameter(v)).Append(')');
        });
        return withNull ? Or([inList, IsNull(expr)])! : inList;
    }

    /// <summary>LIKE with <c>\</c> as escape; case-insensitive as the dialect does it.</summary>
    public static ISqlFragment Like(ISqlFragment expr, string pattern, bool ignoreCase) =>
        new Fragment(w => w.Dialect.WriteLike(w, expr, pattern, ignoreCase));

    public static ISqlFragment Exists(Action<SqlWriter> subquery) => new Fragment(w =>
    {
        w.Append("EXISTS (");
        subquery(w);
        w.Append(')');
    });

    public static ISqlFragment Not(ISqlFragment condition) => new Fragment(w =>
    {
        w.Append("NOT (");
        condition.Render(w);
        w.Append(')');
    });

    /// <summary>AND of the conditions; none means no filter (<see langword="null"/>).</summary>
    public static ISqlFragment? And(IEnumerable<ISqlFragment?> conditions) => Combine(" AND ", conditions);

    /// <summary>OR of the conditions; none means no filter (<see langword="null"/>).</summary>
    public static ISqlFragment? Or(IEnumerable<ISqlFragment?> conditions) => Combine(" OR ", conditions);

    /// <summary>
    /// Translates one criterion on one column. Requests combine with AND, or OR when disjunctive; the server's
    /// restrictions then combine with AND outside that group. A request whose value count does not fit its operator
    /// is ignored; EQ/NE with a null value become IS NULL / IS NOT NULL.
    /// </summary>
    public static ISqlFragment? Translate(ISqlFragment column, Criterion criterion)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(criterion);
        if (!criterion.IsSet && !criterion.IsRestricted)
        {
            return null;
        }

        var requests = Parts(column, criterion, criterion.Predicates);
        var asked = criterion.Disjunctive ? Or(requests) : And(requests);
        if (!criterion.IsRestricted)
        {
            return asked;
        }

        var restrictions = Parts(column, criterion, criterion.Restrictions);
        if (restrictions.Count == 0)
        {
            // A restriction that does not translate must not vanish into "no restriction".
            throw new InvalidOperationException($"The restriction on '{criterion.Name}' could not be translated.");
        }

        return And([asked, .. restrictions]);
    }

    private static List<ISqlFragment> Parts(ISqlFragment column, Criterion criterion, IReadOnlyList<IPredicate> predicates)
    {
        var kind = criterion.ValueKind;
        var parts = new List<ISqlFragment>();
        foreach (var predicate in predicates)
        {
            if (!predicate.Operator.Accepts(predicate.Values.Count))
            {
                continue;
            }

            var values = predicate.Values.Select(kind.ToDatabaseObject).ToList();
            ISqlFragment? part = predicate.Operator switch
            {
                Operator.Eq => values[0] is null ? IsNull(column) : Compare(column, "=", values[0]),
                Operator.Ne => values[0] is null ? IsNotNull(column) : Compare(column, "<>", values[0]),
                Operator.Gt => values[0] is null ? null : Compare(column, ">", values[0]),
                Operator.Ge => values[0] is null ? null : Compare(column, ">=", values[0]),
                Operator.Lt => values[0] is null ? null : Compare(column, "<", values[0]),
                Operator.Le => values[0] is null ? null : Compare(column, "<=", values[0]),
                Operator.Like => values[0] is string p ? Like(column, p, ignoreCase: false) : null,
                Operator.ILike => values[0] is string p ? Like(column, p, ignoreCase: true) : null,
                Operator.Between => values[0] is null || values[1] is null ? null : Between(column, values[0], values[1]),
                Operator.In => In(column, values),
                Operator.IsNull => IsNull(column),
                Operator.IsNotNull => IsNotNull(column),
                _ => null,
            };
            if (part is not null)
            {
                parts.Add(part);
            }
        }

        return parts;
    }

    private static Fragment? Combine(string separator, IEnumerable<ISqlFragment?> conditions)
    {
        var list = conditions.Where(c => c is not null).Cast<ISqlFragment>().ToList();
        return list.Count switch
        {
            0 => null,
            1 => new Fragment(list[0].Render),
            _ => new Fragment(w =>
            {
                w.Append('(');
                w.Join(separator, list, (ww, c) => c.Render(ww));
                w.Append(')');
            }),
        };
    }

    private sealed class Fragment(Action<SqlWriter> render) : ISqlFragment
    {
        public void Render(SqlWriter writer) => render(writer);
    }
}
