namespace Wdc.Domain;

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

    /// <summary>Whether the operator needs actual values: a null there asks nothing (<c>EQ null</c> is <c>IS NULL</c>).</summary>
    public static bool ComparesValues(this Operator op) =>
        op is Operator.Gt or Operator.Ge or Operator.Lt or Operator.Le or Operator.Between or Operator.Like or Operator.ILike;

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
