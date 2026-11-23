using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Wdc.Rest.Server;

/// <summary>Server options of the REST contract.</summary>
public sealed class RestServerOptions
{
    /// <summary>Largest request body read (bytes).</summary>
    public int MaxBodyBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Largest number of items one <c>fetch</c> returns, and the largest <c>pageSize</c>. A <c>fetch</c> with no limit
    /// that would return more is refused (400) instead of silently cut.
    /// </summary>
    public int MaxItems { get; set; } = 1_000;

    /// <summary>
    /// Refuses (409) a write without <c>X-Tx-Id</c> while the same owner has a remote transaction open: it is almost
    /// always a lost header, and the write would bypass the transaction.
    /// </summary>
    public bool RefuseWritesOutsideOpenTransaction { get; set; } = true;

    /// <summary>Who owns a remote transaction: the user (when authenticated) and the client instance.</summary>
    public Func<HttpContext, string?> OwnerOf { get; set; } = DefaultOwner;

    /// <summary><c>user:&lt;id&gt;|client:&lt;X-Client-Id&gt;</c>; either part may be missing; neither → no owner.</summary>
    public static string? DefaultOwner(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
            : null;
        var client = context.Request.Headers[RestNames.ClientHeader].ToString();
        return (user, client.Length == 0 ? null : client) switch
        {
            (null, null) => null,
            (var u, var c) => $"user:{u}|client:{c}",
        };
    }

    /// <summary>
    /// Whose cap of open remote transactions a new one counts against: the user, so a new <c>X-Client-Id</c> doesn't
    /// get around it.
    /// </summary>
    public Func<HttpContext, string?> QuotaOf { get; set; } = DefaultQuota;

    /// <summary><c>user:&lt;id&gt;</c> when authenticated; otherwise the default owner (one anonymous client).</summary>
    public static string? DefaultQuota(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
            : null;
        return user is null ? DefaultOwner(context) : $"user:{user}";
    }
}
