using System.Text.Json;
using Wdc.Domain;

namespace Wdc.Rest.Client;

/// <summary>The remote transaction of one flow.</summary>
internal sealed class RemoteTransactionState(string id)
{
    public string Id { get; } = id;

    public bool Active { get; set; } = true;

    public bool RollbackOnly { get; set; }

    public List<Func<Task>> AfterCommit { get; } = [];
}

/// <summary>
/// <see cref="ITransactionService"/> over the server's remote transactions: the same demarcation as
/// the local database, so a use case runs unchanged on either side. The open transaction follows the calling flow
/// (<see cref="AsyncLocal{T}"/>), and every call of the same <see cref="RestClient"/> in that flow carries its
/// <c>X-Tx-Id</c>.
/// </summary>
/// <remarks>
/// <para>A request that fails inside the transaction makes the server roll it back at commit; the commit then throws
/// <see cref="TransactionSystemException"/> instead of reporting a success that did not happen.</para>
/// <para>When the answer to the commit is lost (network), the outcome is asked with <c>/api/tx/status</c> before
/// anything is reported.</para>
/// </remarks>
public sealed class RemoteTransactionService : ITransactionService
{
    private readonly RestClient _client;
    private readonly string _base;

    public RemoteTransactionService(RestClient client, string basePath = RestNames.TransactionBase)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _base = basePath.TrimEnd('/');
    }

    /// <summary>Whether the calling flow is inside a remote transaction.</summary>
    public bool InTransaction => _client.Ambient.Value is { Active: true };

    public Task<T> Execute<T>(Propagation propagation, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var current = _client.Ambient.Value is { Active: true } state ? state : null;
        return propagation switch
        {
            Propagation.Required => current is null ? RunNew(work, cancellationToken) : work(new Context(current)),
            Propagation.RequiresNew => RunNew(work, cancellationToken),
            Propagation.Mandatory => current is null
                ? throw new TransactionSystemException("MANDATORY: no transaction is open.")
                : work(new Context(current)),
            Propagation.Supports => current is null ? RunWithout(work) : work(new Context(current)),
            Propagation.NotSupported => RunWithout(work),
            Propagation.Never => current is not null
                ? throw new TransactionSystemException("NEVER: a transaction is open.")
                : RunWithout(work),
            _ => throw new ArgumentOutOfRangeException(nameof(propagation), propagation, null),
        };
    }

    public Task AfterCommit(Func<Task> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_client.Ambient.Value is { Active: true } state)
        {
            state.AfterCommit.Add(task);
            return Task.CompletedTask;
        }

        return task();
    }

    private async Task<T> RunNew<T>(Func<ITransactionContext, Task<T>> work, CancellationToken cancellationToken)
    {
        var begun = await _client.SendWithTransaction(HttpMethod.Post, $"{_base}/begin", null, cancellationToken).ConfigureAwait(false);
        var state = new RemoteTransactionState(ReadString(begun!, "txId"));
        _client.Ambient.Value = state; // this flow only: the caller's value is back when we return
        T result;
        try
        {
            result = await work(new Context(state)).ConfigureAwait(false);
        }
        catch
        {
            state.Active = false;
            await SafeRollback(state.Id).ConfigureAwait(false);
            throw; // the work's exception, unchanged
        }

        state.Active = false;
        if (state.RollbackOnly)
        {
            await Finish(state.Id, "rollback", CancellationToken.None).ConfigureAwait(false);
            return result;
        }

        var outcome = await Commit(state.Id).ConfigureAwait(false);
        if (outcome != "committed")
        {
            throw new TransactionSystemException(
                "The transaction was rolled back on the server: a request inside it failed.");
        }

        foreach (var task in state.AfterCommit)
        {
            try
            {
                await task().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // As locally: an after-commit failure does not undo the commit. Nothing to log with here.
            }
        }

        return result;
    }

    private async Task<string> Commit(string id)
    {
        try
        {
            return await Finish(id, "commit", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception lost) when (lost is HttpRequestException or TaskCanceledException)
        {
            // The answer did not arrive: ask what happened instead of guessing.
            string status;
            try
            {
                var body = await _client.SendWithTransaction(HttpMethod.Get, $"{_base}/status", id, CancellationToken.None)
                    .ConfigureAwait(false);
                status = ReadString(body!, "status");
            }
            catch (Exception asking)
            {
                throw new TransactionSystemException("The outcome of the commit is unknown: the server did not answer.",
                    new AggregateException(lost, asking));
            }

            switch (status)
            {
                case "committed":
                case "rolledback":
                    return status;
                case "open":
                    await SafeRollback(id).ConfigureAwait(false);
                    throw new TransactionSystemException("The commit did not reach the server; the transaction was rolled back.", lost);
                default:
                    throw new TransactionSystemException("The outcome of the commit is unknown (the server forgot it).", lost);
            }
        }
    }

    private async Task<string> Finish(string id, string operation, CancellationToken cancellationToken)
    {
        var body = await _client.SendWithTransaction(HttpMethod.Post, $"{_base}/{operation}", id, cancellationToken).ConfigureAwait(false);
        return ReadString(body!, "status");
    }

    private async Task SafeRollback(string id)
    {
        try
        {
            await Finish(id, "rollback", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: the server's reaper rolls it back when idle.
        }
    }

    private async Task<T> RunWithout<T>(Func<ITransactionContext, Task<T>> work)
    {
        _client.Ambient.Value = null; // suspends the caller's transaction for this flow only
        return await work(Context.Inactive).ConfigureAwait(false);
    }

    private static string ReadString(byte[] body, string property)
    {
        string? value = null;
        var reader = RestJson.Start(body);
        RestJson.ReadObject(ref reader, (string name, ref Utf8JsonReader r) =>
        {
            if (name == property && r.TokenType == JsonTokenType.String)
            {
                value = r.GetString();
            }
            else
            {
                r.Skip();
            }
        });
        return value ?? throw new RemoteCallException($"the answer has no '{property}'");
    }

    private sealed class Context : ITransactionContext
    {
        private readonly RemoteTransactionState? _state;

        public Context(RemoteTransactionState state) => _state = state;

        private Context() => _state = null;

        public static Context Inactive { get; } = new();

        public bool IsActive => _state is { Active: true };

        public bool IsRollbackOnly => _state?.RollbackOnly ?? false;

        public void SetRollbackOnly()
        {
            if (_state is null)
            {
                throw new TransactionSystemException("There is no transaction to roll back.");
            }

            _state.RollbackOnly = true;
        }
    }
}
