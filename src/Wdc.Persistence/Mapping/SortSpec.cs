namespace Wdc.Persistence;

/// <summary>A column to sort by.</summary>
public sealed record SortSpec(string Column, bool Descending = false)
{
    public static SortSpec Asc(string column) => new(column);

    public static SortSpec Desc(string column) => new(column, Descending: true);
}
