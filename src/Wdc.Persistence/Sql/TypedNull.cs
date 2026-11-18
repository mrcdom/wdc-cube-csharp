using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>
/// A null that knows the type of its column. Some databases (SQL Server) refuse an untyped null where the column is
/// binary, for example.
/// </summary>
public sealed record TypedNull(Type ClrType)
{
    public static object Of(IValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return new TypedNull(Nullable.GetUnderlyingType(kind.ClrType) ?? kind.ClrType);
    }
}
