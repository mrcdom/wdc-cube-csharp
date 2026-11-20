using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// The transaction a request runs in:
/// <list type="number">
/// <item>with <c>X-Tx-Id</c>: inside that remote transaction — reads too, so they see the transaction's own writes;</item>
/// <item>a write without it while the owner has a remote transaction open: refused (409), see
/// <see cref="RestServerOptions.RefuseWritesOutsideOpenTransaction"/>;</item>
/// <item>any other write: one transaction for the request (REQUIRED); reads: none.</item>
/// </list>
/// </summary>
public static class RestTransactions
{
    public static async Task<T> Run<T>(HttpContext context, bool write, Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(work);
        var services = context.RequestServices;
        var options = RestHttp.Options(context);
        var coordinator = services.GetService<RemoteTransactionCoordinator>();
        var owner = options.OwnerOf(context);

        var id = context.Request.Headers[RestNames.TransactionHeader].ToString();
        if (id.Length > 0)
        {
            if (coordinator is null)
            {
                throw new InvalidRequestException("Remote transactions are not enabled on this server.");
            }

            return await coordinator.Run(id, owner, work, context.RequestAborted);
        }

        if (!write)
        {
            return await work();
        }

        if (coordinator is not null && options.RefuseWritesOutsideOpenTransaction && owner is not null && coordinator.HasOpen(owner))
        {
            throw new TransactionConflictException(
                $"This client has a transaction open: send its {RestNames.TransactionHeader}, or finish it first.");
        }

        var transactions = services.GetService<ITransactionService>();
        return transactions is null
            ? await work()
            : await transactions.Required(_ => work(), context.RequestAborted);
    }
}
