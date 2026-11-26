using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// Holds the transactions remote clients open: each one keeps a connection between requests, is used by
/// one request at a time, belongs to an owner, and is rolled back by the reaper when idle or too old.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A request that fails inside a transaction dooms it: a later commit rolls back and is refused (409).</item>
/// <item>One request at a time: a request or a commit while another request runs is refused (409). A rollback asked
/// then takes effect at once, and the connection is rolled back when that request ends.</item>
/// <item>Repeating the same outcome answers the same; asking the opposite one is a conflict (409); an unknown,
/// expired or finished transaction is gone (410). Never a server error.</item>
/// </list>
/// </remarks>
public sealed class RemoteTransactionCoordinator : IAsyncDisposable
{
    private readonly IDetachedTransactionSource _source;
    private readonly RemoteTransactionOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Entry> _open = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Outcome> _finished = new(StringComparer.Ordinal);
    private readonly Lock _admission = new();
    private long _begun, _committed, _rolledBack, _reaped, _rejected;

    public RemoteTransactionCoordinator(IDetachedTransactionSource source, RemoteTransactionOptions? options = null,
        TimeProvider? time = null, ILogger<RemoteTransactionCoordinator>? logger = null, IMeterFactory? meters = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _options = options ?? new RemoteTransactionOptions();
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        if (meters is not null)
        {
            var meter = meters.Create(RestServerMetrics.MeterName);
            meter.CreateObservableUpDownCounter("wdc.remote.transactions.open", () => _open.Count, "{transaction}",
                "Remote transactions open now.");
            meter.CreateObservableCounter("wdc.remote.transactions", ObserveEvents, "{transaction}",
                "Remote transactions, by what happened to them.");
        }
    }

    private IEnumerable<Measurement<long>> ObserveEvents()
    {
        var stats = Stats;
        yield return new(stats.Begun, new KeyValuePair<string, object?>("event", "begun"));
        yield return new(stats.Committed, new KeyValuePair<string, object?>("event", "committed"));
        yield return new(stats.RolledBack, new KeyValuePair<string, object?>("event", "rolledback"));
        yield return new(stats.Reaped, new KeyValuePair<string, object?>("event", "reaped"));
        yield return new(stats.RejectedByLimit, new KeyValuePair<string, object?>("event", "refused"));
    }

    public RemoteTransactionOptions Options => _options;

    public RemoteTransactionStats Stats =>
        new(_open.Count, Interlocked.Read(ref _begun), Interlocked.Read(ref _committed), Interlocked.Read(ref _rolledBack),
            Interlocked.Read(ref _reaped), Interlocked.Read(ref _rejected));

    /// <summary>Opens a transaction for <paramref name="owner"/> and returns its id.</summary>
    /// <exception cref="TransactionLimitExceededException">Too many open, in total or for the owner.</exception>
    public Task<string> Begin(string? owner, CancellationToken cancellationToken = default) =>
        Begin(owner, owner, cancellationToken);

    /// <summary>
    /// Opens a transaction for <paramref name="owner"/>, counting it against the cap of <paramref name="quota"/>
    /// (the user, when the owner is one user on one client: a new client id doesn't buy a new cap).
    /// </summary>
    /// <exception cref="TransactionLimitExceededException">Too many open, in total or for the quota.</exception>
    public async Task<string> Begin(string? owner, string? quota, CancellationToken cancellationToken = default)
    {
        Reserve(quota);
        IDetachedTransaction transaction;
        try
        {
            transaction = await _source.BeginDetached(cancellationToken);
        }
        catch
        {
            ReleaseReservation(quota);
            throw;
        }

        var id = Guid.NewGuid().ToString("N");
        _open[id] = new Entry(id, owner, quota, transaction, _time.GetUtcNow());
        ReleaseReservation(quota);
        Interlocked.Increment(ref _begun);
        return id;
    }

    /// <summary>Runs one request inside the transaction <paramref name="id"/>.</summary>
    /// <exception cref="TransactionNotFoundException">Unknown, expired or finished.</exception>
    /// <exception cref="TransactionConflictException">Another request is using it.</exception>
    /// <exception cref="AccessDeniedException">It belongs to someone else.</exception>
    public async Task<T> Run<T>(string id, string? owner, Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var entry = Find(id, owner);
        if (!entry.Gate.Wait(0))
        {
            throw InUse();
        }

        T result;
        try
        {
            if (entry.Closed)
            {
                throw Gone(id);
            }

            entry.Touch(_time.GetUtcNow());
            try
            {
                result = await entry.Transaction.Run(work);
            }
            catch
            {
                entry.Transaction.SetRollbackOnly(); // a failed request never leaves half its writes to commit
                throw;
            }
            finally
            {
                entry.Touch(_time.GetUtcNow());
            }
        }
        finally
        {
            await RollBackIfAsked(entry);
            entry.Gate.Release();
            await RollBackIfAskedJustNow(entry);
        }

        return result;
    }

    /// <summary>Commits; when a request inside it failed, rolls back instead and refuses.</summary>
    /// <returns><see cref="RemoteTransactionStatus.Committed"/>.</returns>
    /// <exception cref="TransactionConflictException">A request inside it failed (it was rolled back), or another
    /// request is using it, or it was rolled back.</exception>
    /// <exception cref="TransactionNotFoundException">Unknown or expired.</exception>
    public Task<RemoteTransactionStatus> Commit(string id, string? owner, CancellationToken cancellationToken = default) =>
        Finish(id, owner, commit: true, cancellationToken);

    /// <summary>Rolls back; while a request runs, at once for every later call, and on the connection when it ends.</summary>
    public Task<RemoteTransactionStatus> Rollback(string id, string? owner, CancellationToken cancellationToken = default) =>
        Finish(id, owner, commit: false, cancellationToken);

    public RemoteTransactionStatus Status(string id, string? owner)
    {
        if (_open.TryGetValue(id, out var entry))
        {
            CheckOwner(entry.Owner, owner);
            return RemoteTransactionStatus.Open;
        }

        if (_finished.TryGetValue(id, out var outcome))
        {
            CheckOwner(outcome.Owner, owner);
            return outcome.Status;
        }

        return RemoteTransactionStatus.Unknown;
    }

    /// <summary>Whether <paramref name="owner"/> has a transaction open (a write without its header is refused).</summary>
    public bool HasOpen(string owner) => _open.Values.Any(e => e.Owner == owner);

    /// <summary>Rolls back what is idle or too old and forgets old outcomes. Called by the reaper.</summary>
    public async Task Reap(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        foreach (var entry in _open.Values)
        {
            var expired = now - entry.LastUsed >= _options.IdleTimeout || now - entry.CreatedAt >= _options.MaxLifetime;
            if (!expired || !entry.Gate.Wait(0)) // in use: looked at again next round
            {
                continue;
            }

            try
            {
                if (_open.TryRemove(entry.Id, out _))
                {
                    entry.Closed = true;
                    await entry.Transaction.Rollback(cancellationToken);
                    Remember(entry, RemoteTransactionStatus.RolledBack, now);
                    Interlocked.Increment(ref _reaped);
                    _logger.LogInformation("Remote transaction {Id} rolled back by the reaper (idle or too old).", entry.Id);
                }
            }
            catch (Exception caught)
            {
                _logger.LogError(caught, "Reaping remote transaction {Id} failed.", entry.Id);
            }
            finally
            {
                entry.Gate.Release();
            }
        }

        foreach (var (id, outcome) in _finished)
        {
            if (now - outcome.At >= _options.OutcomeRetention)
            {
                _finished.TryRemove(id, out _);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _open.Values)
        {
            if (_open.TryRemove(entry.Id, out _))
            {
                entry.Closed = true;
                await entry.Transaction.DisposeAsync();
            }
        }
    }

    // :: Internals

    private int _reserved;
    private readonly Dictionary<string, int> _reservedByQuota = new(StringComparer.Ordinal);

    private void Reserve(string? quota)
    {
        lock (_admission)
        {
            if (_open.Count + _reserved >= _options.MaxOpen)
            {
                Interlocked.Increment(ref _rejected);
                throw new TransactionLimitExceededException($"Too many open transactions (limit {_options.MaxOpen}).");
            }

            if (quota is not null)
            {
                var mine = _open.Values.Count(e => e.Quota == quota) + _reservedByQuota.GetValueOrDefault(quota);
                if (mine >= _options.MaxOpenPerOwner)
                {
                    Interlocked.Increment(ref _rejected);
                    throw new TransactionLimitExceededException(
                        $"Too many open transactions for this user (limit {_options.MaxOpenPerOwner}).");
                }

                _reservedByQuota[quota] = _reservedByQuota.GetValueOrDefault(quota) + 1;
            }

            _reserved++;
        }
    }

    private void ReleaseReservation(string? quota)
    {
        lock (_admission)
        {
            _reserved--;
            if (quota is not null && _reservedByQuota.TryGetValue(quota, out var count))
            {
                if (count <= 1)
                {
                    _reservedByQuota.Remove(quota);
                }
                else
                {
                    _reservedByQuota[quota] = count - 1;
                }
            }
        }
    }

    private Entry Find(string id, string? owner)
    {
        if (_open.TryGetValue(id, out var entry))
        {
            CheckOwner(entry.Owner, owner);
            return entry;
        }

        throw Gone(id);
    }

    private TransactionNotFoundException Gone(string id) =>
        _finished.TryGetValue(id, out var outcome)
            ? new TransactionNotFoundException($"The transaction is already {Describe(outcome.Status)}.")
            : new TransactionNotFoundException("Unknown or expired transaction.");

    private static TransactionConflictException InUse() =>
        new("The transaction is in use by another request.");

    private static TransactionConflictException Doomed() =>
        new("The transaction was rolled back: a request inside it failed.");

    // A rollback asked just as the gate opened: whoever takes the gate does it, once.
    private async Task RollBackIfAskedJustNow(Entry entry)
    {
        if (Volatile.Read(ref entry.RollbackAsked) == 1 && entry.Gate.Wait(0))
        {
            try
            {
                await RollBackIfAsked(entry);
            }
            finally
            {
                entry.Gate.Release();
            }
        }
    }

    // Not cancellable: a request that was aborted must still not leave its connection open.
    private async Task RollBackIfAsked(Entry entry)
    {
        if (Interlocked.Exchange(ref entry.RollbackAsked, 0) == 0)
        {
            return;
        }

        try
        {
            await entry.Transaction.Rollback(CancellationToken.None);
        }
        catch (Exception caught)
        {
            _logger.LogError(caught, "Rolling back remote transaction {Id} after its request ended failed.", entry.Id);
        }
    }

    private async Task<RemoteTransactionStatus> Finish(string id, string? owner, bool commit, CancellationToken cancellationToken)
    {
        if (_open.TryGetValue(id, out var entry))
        {
            CheckOwner(entry.Owner, owner);
            if (!entry.Gate.Wait(0))
            {
                if (commit)
                {
                    throw InUse();
                }

                // Rolled back for every later call now; the connection, when the request in progress ends.
                if (!entry.Closed && _open.TryRemove(id, out _))
                {
                    entry.Closed = true;
                    Remember(entry, RemoteTransactionStatus.RolledBack, _time.GetUtcNow());
                    Interlocked.Increment(ref _rolledBack);
                    Volatile.Write(ref entry.RollbackAsked, 1);
                    await RollBackIfAskedJustNow(entry); // the request may have ended meanwhile

                    return RemoteTransactionStatus.RolledBack;
                }
            }
            else
            {
                try
                {
                    if (!entry.Closed && _open.TryRemove(id, out _))
                    {
                        entry.Closed = true;
                        return await Close(entry, commit, cancellationToken);
                    }
                }
                finally
                {
                    entry.Gate.Release();
                }
            }
        }

        // Finished already (by another call, or the reaper): repeating the same outcome answers the same.
        if (_finished.TryGetValue(id, out var outcome))
        {
            CheckOwner(outcome.Owner, owner);
            if (commit && outcome.Status == RemoteTransactionStatus.Committed)
            {
                return outcome.Status;
            }

            if (commit && outcome.ByCommit)
            {
                throw Doomed();
            }

            if (!commit && outcome.Status == RemoteTransactionStatus.RolledBack)
            {
                return outcome.Status;
            }

            throw new TransactionConflictException($"The transaction is already {Describe(outcome.Status)}.");
        }

        throw Gone(id);
    }

    private async Task<RemoteTransactionStatus> Close(Entry entry, bool commit, CancellationToken cancellationToken)
    {
        RemoteTransactionStatus status;
        try
        {
            if (commit)
            {
                status = await entry.Transaction.Commit(cancellationToken)
                    ? RemoteTransactionStatus.Committed
                    : RemoteTransactionStatus.RolledBack; // rollback-only: a request inside it failed
            }
            else
            {
                await entry.Transaction.Rollback(cancellationToken);
                status = RemoteTransactionStatus.RolledBack;
            }
        }
        catch
        {
            // The commit failed and the database rolled back: a status query must tell the truth.
            Remember(entry, RemoteTransactionStatus.RolledBack, _time.GetUtcNow(), commit);
            Interlocked.Increment(ref _rolledBack);
            throw;
        }

        Remember(entry, status, _time.GetUtcNow(), commit);
        Interlocked.Increment(ref status == RemoteTransactionStatus.Committed ? ref _committed : ref _rolledBack);
        return commit && status == RemoteTransactionStatus.RolledBack ? throw Doomed() : status;
    }

    private void Remember(Entry entry, RemoteTransactionStatus status, DateTimeOffset at, bool byCommit = false) =>
        _finished[entry.Id] = new Outcome(status, entry.Owner, at, byCommit);

    private static void CheckOwner(string? expected, string? actual)
    {
        if (expected is not null && !string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new AccessDeniedException("The transaction belongs to another client.");
        }
    }

    private static string Describe(RemoteTransactionStatus status) => status switch
    {
        RemoteTransactionStatus.Committed => "committed",
        RemoteTransactionStatus.RolledBack => "rolled back",
        _ => "finished",
    };

    private sealed class Entry(string id, string? owner, string? quota, IDetachedTransaction transaction, DateTimeOffset createdAt)
    {
        private long _lastUsedTicks = createdAt.UtcTicks;

        public string Id { get; } = id;

        public string? Owner { get; } = owner;

        public string? Quota { get; } = quota;

        public IDetachedTransaction Transaction { get; } = transaction;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public DateTimeOffset LastUsed => new(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero);

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public volatile bool Closed;

        /// <summary>1 when a rollback was asked while a request ran: done once, by whoever holds the gate next.</summary>
        public int RollbackAsked;

        public void Touch(DateTimeOffset now) => Interlocked.Exchange(ref _lastUsedTicks, now.UtcTicks);
    }

    private sealed record Outcome(RemoteTransactionStatus Status, string? Owner, DateTimeOffset At, bool ByCommit);
}
