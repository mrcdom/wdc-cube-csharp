namespace Wdc.Domain;

/// <summary>
/// A collection in a projection: a list holding one element, the shape of each item, plus the criteria, limit and
/// offset that filter, order and slice the collection.
/// </summary>
public sealed class ProjectionList<T> : List<T>, IProjectionList
    where T : class
{
    public ProjectionList(T shape, object? criteria = null, int? limit = null, int? offset = null)
        : base(1)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (limit is < 0 || offset is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit and offset cannot be negative.");
        }

        Add(shape);
        Criteria = criteria;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>The shape of each item.</summary>
    public T Shape => this[0];

    /// <summary>The child criteria (filter and order), or <see langword="null"/>.</summary>
    public object? Criteria { get; }

    public int? Limit { get; }

    public int? Offset { get; }

    object IProjectionList.Shape => Shape;
}

/// <summary>A projection list seen without its item type.</summary>
internal interface IProjectionList
{
    object Shape { get; }

    object? Criteria { get; }

    int? Limit { get; }

    int? Offset { get; }
}
