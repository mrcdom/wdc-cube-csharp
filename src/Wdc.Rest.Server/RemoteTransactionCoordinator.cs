using System.Collections.Concurrent;
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
/// <item>A request that fails inside a transaction marks it rollback-only: a later commit rolls back and says so.</item>
/// <item>Commit and rollback wait for the request in progress (they never close the connection under it).</item>
/// <item>Repeating the same outcome is a no-op; asking the opposite one, or for an unknown transaction, is a conflict
/// (409), never a server error.</item>
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
        TimeProvider? time = null, ILogger<RemoteTransactionCoordinator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _options = options ?? new RemoteTransactionOptions();
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public RemoteTransactionOptions Options => _options;

    public RemoteTransactionStats Stats =>
        new(_open.Count, Interlocked.Read(ref _begun), Interlocked.Read(ref _committed), Interlocked.Read(ref _rolledBack),
            Interlocked.Read(ref _reaped), Interlocked.Read(ref _rejected));

    /// <summary>Opens a transaction for <paramref name="owner"/> and returns its id.</summary>
    /// <exception cref="TransactionLimitExceededException">Too many open, in total or for the owner.</exception>
    public async Task<string> Begin(string? owner, CancellationToken cancellationToken = default)
    {
        Reserve(owner);
        IDetachedTransaction transaction;
        try
        {
            transaction = await _source.BeginDetached(cancellationToken);
        }
        catch
        {
            ReleaseReservation(owner);
            throw;
        }

        var id = Guid.NewGuid().ToString("N");
        _open[id] = new Entry(id, owner, transaction, _time.GetUtcNow());
        ReleaseReservation(owner);
        Interlocked.Increment(ref _begun);
        return id;
    }

    /// <summary>Runs one request inside the transaction <paramref name="id"/>.</summary>
    /// <exception cref="TransactionConflictException">Unknown, expired or finished.</exception>
    /// <exception cref="AccessDeniedException">It belongs to someone else.</exception>
    public async Task<T> Run<T>(string id, string? owner, Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var entry = Find(id, owner);
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (entry.Closed)
            {
                throw Gone(id);
            }

            entry.Touch(_time.GetUtcNow());
            try
            {
                return await entry.Transaction.Run(work);
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
            entry.Gate.Release();
        }
    }

    /// <summary>Commits; when a request inside it failed, rolls back instead.</summary>
    /// <returns>The outcome: <see cref="RemoteTransactionStatus.Committed"/> or <see cref="RemoteTransactionStatus.RolledBack"/>.</returns>
    public Task<RemoteTransactionStatus> Commit(string id, string? owner, CancellationToken cancellationToken = default) =>
        Finish(id, owner, commit: true, cancellationToken);

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
    private readonly Dictionary<string, int> _reservedByOwner = new(StringComparer.Ordinal);

    private void Reserve(string? owner)
    {
        lock (_admission)
        {
            if (_open.Count + _reserved >= _options.MaxOpen)
            {
                Interlocked.Increment(ref _rejected);
                throw new TransactionLimitExceededException($"Too many open transactions (limit {_options.MaxOpen}).");
            }

            if (owner is not null)
            {
                var mine = _open.Values.Count(e => e.Owner == owner) + _reservedByOwner.GetValueOrDefault(owner);
                if (mine >= _options.MaxOpenPerOwner)
                {
                    Interlocked.Increment(ref _rejected);
                    throw new TransactionLimitExceededException(
                        $"Too many open transactions for this client (limit {_options.MaxOpenPerOwner}).");
                }

                _reservedByOwner[owner] = _reservedByOwner.GetValueOrDefault(owner) + 1;
            }

            _reserved++;
        }
    }

    private void ReleaseReservation(string? owner)
    {
        lock (_admission)
        {
            _reserved--;
            if (owner is not null && _reservedByOwner.TryGetValue(owner, out var count))
            {
                if (count <= 1)
                {
                    _reservedByOwner.Remove(owner);
                }
                else
                {
                    _reservedByOwner[owner] = count - 1;
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

    private TransactionConflictException Gone(string id) =>
        _finished.TryGetValue(id, out var outcome)
            ? new TransactionConflictException($"The transaction is already {Describe(outcome.Status)}.")
            : new TransactionConflictException("Unknown or expired transaction.");

    private async Task<RemoteTransactionStatus> Finish(string id, string? owner, bool commit, CancellationToken cancellationToken)
    {
        if (_open.TryGetValue(id, out var entry))
        {
            CheckOwner(entry.Owner, owner);
            await entry.Gate.WaitAsync(cancellationToken); // waits for the request in progress
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

        // Finished already (by another call, or the reaper): repeating the same outcome is a no-op.
        if (_finished.TryGetValue(id, out var outcome))
        {
            CheckOwner(outcome.Owner, owner);
            var same = commit
                ? outcome.Status == RemoteTransactionStatus.Committed || outcome.ByCommit
                : outcome.Status == RemoteTransactionStatus.RolledBack;
            if (same)
            {
                return outcome.Status;
            }
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
        return status;
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

    private sealed class Entry(string id, string? owner, IDetachedTransaction transaction, DateTimeOffset createdAt)
    {
        private long _lastUsedTicks = createdAt.UtcTicks;

        public string Id { get; } = id;

        public string? Owner { get; } = owner;

        public IDetachedTransaction Transaction { get; } = transaction;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public DateTimeOffset LastUsed => new(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero);

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public volatile bool Closed;

        public void Touch(DateTimeOffset now) => Interlocked.Exchange(ref _lastUsedTicks, now.UtcTicks);
    }

    private sealed record Outcome(RemoteTransactionStatus Status, string? Owner, DateTimeOffset At, bool ByCommit);
}
