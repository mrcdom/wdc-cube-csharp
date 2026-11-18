namespace Wdc.Domain;

/// <summary>One comparison request: an operator and its values.</summary>
public interface IPredicate
{
    Operator Operator { get; }

    /// <summary>The values, boxed.</summary>
    IReadOnlyList<object?> Values { get; }
}
