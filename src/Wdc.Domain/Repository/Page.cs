namespace Wdc.Domain;

/// <summary>One page of a listing.</summary>
/// <param name="Items">The items of this page.</param>
/// <param name="Number">The page, zero-based.</param>
/// <param name="TotalPages">How many pages exist.</param>
/// <param name="TotalItems">How many items exist.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int Number, int TotalPages, int TotalItems)
{
    public static Page<T> Of(IReadOnlyList<T> items, int page, int pageSize, int totalItems)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var totalPages = totalItems == 0 ? 0 : (totalItems + pageSize - 1) / pageSize;
        return new Page<T>(items, page, totalPages, totalItems);
    }
}
