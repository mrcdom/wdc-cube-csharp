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
