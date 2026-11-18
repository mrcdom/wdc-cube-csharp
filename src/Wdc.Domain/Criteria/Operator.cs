namespace Wdc.Domain;

/// <summary>A comparison a criterion can ask for.</summary>
public enum Operator
{
    Eq,
    Ne,
    Gt,
    Ge,
    Lt,
    Le,
    Like,
    ILike,
    Between,
    In,
    IsNull,
    IsNotNull,
}

/// <summary>Arity and wire names (<c>EQ</c>, <c>IS_NULL</c>... — the frozen contract shared with the Kotlin version).</summary>
public static class Operators
{
    private static readonly Dictionary<string, Operator> s_byWireName =
        System.Enum.GetValues<Operator>().ToDictionary(WireName, op => op, StringComparer.Ordinal);

    /// <summary>How many values the operator takes; -1 for any number (IN).</summary>
    public static int Arity(this Operator op) => op switch
    {
        Operator.Between => 2,
        Operator.In => -1,
        Operator.IsNull or Operator.IsNotNull => 0,
        _ => 1,
    };

    /// <summary>Whether <paramref name="count"/> values fit the operator.</summary>
    public static bool Accepts(this Operator op, int count) => op.Arity() < 0 ? count >= 0 : count == op.Arity();

    public static string WireName(this Operator op) => op switch
    {
        Operator.Eq => "EQ",
        Operator.Ne => "NE",
        Operator.Gt => "GT",
        Operator.Ge => "GE",
        Operator.Lt => "LT",
        Operator.Le => "LE",
        Operator.Like => "LIKE",
        Operator.ILike => "ILIKE",
        Operator.Between => "BETWEEN",
        Operator.In => "IN",
        Operator.IsNull => "IS_NULL",
        Operator.IsNotNull => "IS_NOT_NULL",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    /// <summary>An operator this side does not know is discarded, never approximated.</summary>
    public static bool TryParseWireName(string? name, out Operator op) =>
        s_byWireName.TryGetValue(name ?? string.Empty, out op);
}
