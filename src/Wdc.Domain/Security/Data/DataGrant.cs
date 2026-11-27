namespace Wdc.Domain;

/// <summary>
/// What one role may do with one entity, over one set of rows: which fields it reads as they are, which it reads
/// masked, which it may set when creating and when updating, and whether it may delete.
/// </summary>
/// <remarks>
/// The key is not declared: whoever reads a row reads its key, and it is never written. A reference or a collection is
/// one field here: what is read inside it is up to the policy of the related entity. A role that reaches different
/// rows with different fields (its own user in full, the others partially) has one grant for each.
/// </remarks>
public sealed class DataGrant<TEntity, TCriteria>
    where TEntity : class
{
    internal DataGrant(IRowScope<TEntity, TCriteria> rows, IReadOnlySet<string> read, IReadOnlyDictionary<string, Mask> masked,
        IReadOnlySet<string> create, IReadOnlySet<string> update, bool delete)
    {
        Rows = rows;
        Read = read;
        Masked = masked;
        Create = create;
        Update = update;
        Delete = delete;
    }

    /// <summary>The rows the grant reaches.</summary>
    public IRowScope<TEntity, TCriteria> Rows { get; }

    /// <summary>The fields read as they are; also the ones the grant may filter and sort by.</summary>
    public IReadOnlySet<string> Read { get; }

    /// <summary>The fields read through a mask.</summary>
    public IReadOnlyDictionary<string, Mask> Masked { get; }

    /// <summary>The fields that may be set when creating a row.</summary>
    public IReadOnlySet<string> Create { get; }

    /// <summary>The fields that may be changed in a reachable row.</summary>
    public IReadOnlySet<string> Update { get; }

    /// <summary>Whether the reachable rows may be deleted.</summary>
    public bool Delete { get; }

    /// <summary>Whether the grant reads the entity at all.</summary>
    public bool Reads => Read.Count > 0 || Masked.Count > 0;

    /// <summary>Whether it reads the field, as it is or masked.</summary>
    public bool Sees(string field) => Read.Contains(field) || Masked.ContainsKey(field);

    public override string ToString() =>
        $"DataGrant(rows={Rows}, read=[{string.Join(", ", Read)}], masked=[{string.Join(", ", Masked.Keys)}], " +
        $"create=[{string.Join(", ", Create)}], update=[{string.Join(", ", Update)}], delete={Delete})";
}
