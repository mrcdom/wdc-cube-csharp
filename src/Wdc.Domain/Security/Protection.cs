using System.Collections.Concurrent;

namespace Wdc.Domain;

/// <summary>
/// What the protected repositories of one session share: who is acting, the transactions, and each other — a write
/// that cascades to a child applies the child's policy (decision D-24).
/// </summary>
public sealed class Protection
{
    private readonly ConcurrentDictionary<Type, ICascadePolicy> _policies = new();

    public Protection(IActorSource actors, ITransactionService transactions)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(transactions);
        Actors = actors;
        Transactions = transactions;
    }

    public IActorSource Actors { get; }

    public ITransactionService Transactions { get; }

    /// <summary>Who is acting; nobody is refused.</summary>
    /// <exception cref="AuthenticationException">Nobody is acting.</exception>
    public Actor RequireActor() => Actors.Current ?? throw new AuthenticationException("Authentication required.");

    internal void Add(Type entityType, ICascadePolicy policy) => _policies[entityType] = policy;

    /// <summary>Runs a write in a transaction, with the children it writes in cascade answering to their own policies.</summary>
    internal Task<T> Write<T>(Actor actor, Func<Task<T>> work, CancellationToken cancellationToken) =>
        Transactions.Execute(Propagation.Required,
            _ => CascadeGuard.Run(new PolicyCascadeGuard(this, actor), work), cancellationToken);

    private ICascadePolicy PolicyOf(Type entityType) =>
        _policies.GetValueOrDefault(entityType)
        ?? throw new AccessDeniedException($"{entityType.Name} is not protected here: it cannot be written through another entity.");

    private sealed class PolicyCascadeGuard(Protection protection, Actor actor) : ICascadeGuard
    {
        public Task BeforeInsert(Type entityType, object entity, CancellationToken cancellationToken) =>
            protection.PolicyOf(entityType).CheckInsert(actor, entity, cancellationToken);

        public Task BeforeUpdate(Type entityType, object entity, object projection, CancellationToken cancellationToken) =>
            protection.PolicyOf(entityType).CheckUpdate(actor, entity, projection, cancellationToken);

        public Task BeforeDelete(Type entityType, IReadOnlyList<object> keys, CancellationToken cancellationToken) =>
            protection.PolicyOf(entityType).CheckDelete(actor, keys, cancellationToken);
    }
}

/// <summary>A protected repository, as the policy of a child written in cascade.</summary>
internal interface ICascadePolicy
{
    Task CheckInsert(Actor actor, object entity, CancellationToken cancellationToken);

    Task CheckUpdate(Actor actor, object entity, object projection, CancellationToken cancellationToken);

    Task CheckDelete(Actor actor, IReadOnlyList<object> keys, CancellationToken cancellationToken);
}
