using Microsoft.AspNetCore.Http;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// Installed around a write request: each child the repository writes in cascade goes through the rules of its own
/// routes. A child with no routes cannot be written through another entity.
/// </summary>
internal sealed class RpcCascadeGuard(HttpContext context, RpcRegistry registry) : ICascadeGuard
{
    public Task BeforeInsert(Type entityType, object entity, CancellationToken cancellationToken) =>
        Rules(entityType).CheckInsert(context, entity);

    public Task BeforeUpdate(Type entityType, object entity, object projection, CancellationToken cancellationToken) =>
        Rules(entityType).CheckUpdate(context, entity, projection);

    public Task BeforeDelete(Type entityType, IReadOnlyList<object> keys, CancellationToken cancellationToken) =>
        Rules(entityType).CheckDelete(context, keys);

    private ICascadeRules Rules(Type entityType) =>
        registry.RulesOf(entityType)
        ?? throw new AccessDeniedException($"{entityType.Name} is not exposed: it cannot be written through another entity.");
}
