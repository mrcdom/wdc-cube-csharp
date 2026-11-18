using Microsoft.AspNetCore.Http;

namespace Wdc.Rest.Server;

/// <summary>The rules of an entity's routes, applied to a child written in cascade.</summary>
internal interface ICascadeRules
{
    Task CheckInsert(HttpContext context, object entity);

    Task CheckUpdate(HttpContext context, object entity, object projection);

    Task CheckDelete(HttpContext context, IReadOnlyList<object> keys);
}
