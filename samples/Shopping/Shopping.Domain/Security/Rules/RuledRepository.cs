using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>A repository that passes everything to <paramref name="inner"/>, for a rule to override a write.</summary>
internal abstract class RuledRepository<TEntity, TCriteria, TKey>(IRepository<TEntity, TCriteria, TKey> inner, IActorSource actors)
    : IRepository<TEntity, TCriteria, TKey>
    where TEntity : class
    where TCriteria : ICriteria
{
    protected IRepository<TEntity, TCriteria, TKey> Inner { get; } = inner;

    /// <summary>Whether whoever acts maintains the solution: the rules marked as relaxable are relaxed for them.</summary>
    protected bool Maintains => actors.Current is { } actor && Permissions.Maintains(actor);

    protected Actor Actor => actors.Current ?? throw new AuthenticationException("Authentication required.");

    public Task<int> Count(TCriteria criteria, CancellationToken cancellationToken = default) => Inner.Count(criteria, cancellationToken);

    public Task<IReadOnlyList<TEntity>> Fetch(TCriteria criteria, int offset = 0, int limit = 0, CancellationToken cancellationToken = default) =>
        Inner.Fetch(criteria, offset, limit, cancellationToken);

    public Task<Page<TEntity>> FetchPage(TCriteria criteria, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Inner.FetchPage(criteria, page, pageSize, cancellationToken);

    public Task<TEntity?> FetchById(TKey id, TEntity? projection = null, CancellationToken cancellationToken = default) =>
        Inner.FetchById(id, projection, cancellationToken);

    public TEntity OwnFieldsProjection() => Inner.OwnFieldsProjection();

    public virtual Task<bool> Insert(TEntity bean, TEntity? projection = null, CancellationToken cancellationToken = default) =>
        Inner.Insert(bean, projection, cancellationToken);

    public virtual Task<bool> Update(TEntity newBean, TEntity? oldBean = null, TEntity? projection = null,
        CancellationToken cancellationToken = default) =>
        Inner.Update(newBean, oldBean, projection, cancellationToken);

    public virtual Task<int> Delete(TCriteria criteria, CancellationToken cancellationToken = default) => Inner.Delete(criteria, cancellationToken);

    /// <summary>What an update writes: the projection, or the fields held (or changed against the old bean).</summary>
    protected static TEntity Written(EntityModel<TEntity> model, TEntity newBean, TEntity? oldBean, TEntity? projection) =>
        projection ?? (oldBean is null ? model.HeldFields(newBean) : model.ChangedFields(newBean, oldBean));
}
