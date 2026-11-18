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
