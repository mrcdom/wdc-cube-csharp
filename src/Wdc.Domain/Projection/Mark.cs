namespace Wdc.Domain;

/// <summary>
/// Markers for projection beans: a field holding one of these means "wanted". The value itself is never read.
/// <code>var projection = new Purchase { Id = Mark.Int64, BuyDate = Mark.Instant, User = new User { Id = Mark.Int64 } };</code>
/// </summary>
public static class Mark
{
    public const int Int32 = 1;
    public const long Int64 = 1L;
    public const double Double = 1d;
    public const decimal Decimal = 1m;
    public const string String = "~";
    public const bool Boolean = true;

    public static DateTimeOffset Instant => DateTimeOffset.UnixEpoch;

    public static DateOnly Date => new(1970, 1, 1);

    public static Guid Uuid => Guid.Empty;

    public static byte[] Bytes => [];

    public static TEnum Enum<TEnum>()
        where TEnum : struct, System.Enum => default;

    /// <summary>A collection projection: the shape of each item, optionally filtered, ordered and sliced.</summary>
    public static ProjectionList<T> List<T>(T shape, object? criteria = null, int? limit = null, int? offset = null)
        where T : class => new(shape, criteria, limit, offset);
}

/// <summary>
/// A collection in a projection: a list holding one element, the shape of each item, plus the criteria, limit and
/// offset that filter, order and slice the collection.
/// </summary>
public sealed class ProjectionList<T> : List<T>
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
}
