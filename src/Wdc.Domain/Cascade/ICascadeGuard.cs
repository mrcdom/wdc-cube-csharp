namespace Wdc.Domain;

/// <summary>
/// Watches the writes a repository makes in cascade — the children it inserts, updates or deletes because the
/// projection of the entity being written asked for them (decision D-23). Rules that hold per entity (the policies of
/// the protected repositories, decision D-24) apply to those children through it. The entity the caller wrote directly is not
/// reported: its own rules already ran.
/// </summary>
public interface ICascadeGuard
{
    /// <summary>Before a child is inserted in cascade. May complete it (a price from the product) or refuse.</summary>
    Task BeforeInsert(Type entityType, object entity, CancellationToken cancellationToken);

    /// <summary>Before a child is updated in cascade; <paramref name="projection"/> marks the fields written.</summary>
    Task BeforeUpdate(Type entityType, object entity, object projection, CancellationToken cancellationToken);

    /// <summary>Before children are deleted in cascade (orphans of a full-state update, or a declared cascade delete).</summary>
    Task BeforeDelete(Type entityType, IReadOnlyList<object> keys, CancellationToken cancellationToken);
}
