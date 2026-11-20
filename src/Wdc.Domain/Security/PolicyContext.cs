namespace Wdc.Domain;

/// <summary>What a rule of a <see cref="RepositoryPolicy{TEntity,TCriteria,TKey}"/> is told about the call.</summary>
/// <param name="Actor">Who is acting.</param>
/// <param name="InCascade">
/// Whether the entity is being written because another one's projection asked for it: a child inserted
/// with its parent, or an orphan deleted by the parent's full-state update.
/// </param>
public sealed record PolicyContext(Actor Actor, bool InCascade);
