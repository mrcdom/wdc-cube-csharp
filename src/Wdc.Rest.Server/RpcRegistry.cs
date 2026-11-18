using System.Collections.Concurrent;

namespace Wdc.Rest.Server;

/// <summary>
/// The repository routes of the application, by entity type: a write that cascades to a child applies the rules of the
/// child's routes (decision D-23). Registered by <see cref="RestServerServiceCollectionExtensions.AddCubeRest"/>;
/// each <see cref="RepositoryRpc{TEntity,TCriteria,TKey}"/> adds itself when mapped.
/// </summary>
public sealed class RpcRegistry
{
    private readonly ConcurrentDictionary<Type, ICascadeRules> _rules = new();

    internal void Add(Type entityType, ICascadeRules rules) => _rules[entityType] = rules;

    internal ICascadeRules? RulesOf(Type entityType) => _rules.GetValueOrDefault(entityType);
}
