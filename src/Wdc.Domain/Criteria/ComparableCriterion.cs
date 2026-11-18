namespace Wdc.Domain;

/// <summary>A filter on an ordered field: adds greater/less than and between.</summary>
public class ComparableCriterion<TOwner, T> : Criterion<TOwner, T>
    where TOwner : class
{
    public ComparableCriterion(TOwner owner, string name, ValueKind<T> kind)
        : base(owner, name, kind)
    {
    }

    public override ComparableCriterion<TOwner, T> Or()
    {
        base.Or();
        return this;
    }

    public override ComparableCriterion<TOwner, T> And()
    {
        base.And();
        return this;
    }

    public TOwner Gt(T value) => ValueKind<T>.IsNull(value) ? Owner : Add(Operator.Gt, value);

    public TOwner Ge(T value) => ValueKind<T>.IsNull(value) ? Owner : Add(Operator.Ge, value);

    public TOwner Lt(T value) => ValueKind<T>.IsNull(value) ? Owner : Add(Operator.Lt, value);

    public TOwner Le(T value) => ValueKind<T>.IsNull(value) ? Owner : Add(Operator.Le, value);

    /// <summary>Between, inclusive. An open end becomes GE or LE; both open asks nothing.</summary>
    public TOwner Between(T start, T end) => (ValueKind<T>.IsNull(start), ValueKind<T>.IsNull(end)) switch
    {
        (true, true) => Owner,
        (true, false) => Le(end),
        (false, true) => Ge(start),
        _ => Add(Operator.Between, start, end),
    };
}
