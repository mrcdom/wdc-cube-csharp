namespace Wdc.Domain;

/// <summary>What a field holds.</summary>
public enum FieldKind
{
    /// <summary>A single value (number, text, date...).</summary>
    Scalar,

    /// <summary>Another entity (N:1 or 1:1).</summary>
    Reference,

    /// <summary>A list of other entities (1:N).</summary>
    Collection,
}
