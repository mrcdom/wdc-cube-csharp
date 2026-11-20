using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>
/// A database: its dialect, how to open a connection, and the ambient transaction.
/// </summary>
/// <remarks>
/// <para>
/// The open transaction lives in an <see cref="AsyncLocal{T}"/>: it follows every <c>await</c> of the unit of work,
/// and repositories find it without a parameter, JPA-style.
/// </para>
/// <para>
/// <b>Writes without a transaction run in one of their own</b> (implicit REQUIRED), whoever calls them. Reads without a
/// transaction use a connection of their own in autocommit.
/// </para>
/// <para>
/// One transaction is one connection: run the statements of a unit of work one after the other, not in parallel.
/// </para>
/// </remarks>
public sealed class Database : ITransactionService, IDetachedTransactionSource
{
    private readonly Func<CancellationToken, ValueTask<DbConnection>> _open;
    private readonly AsyncLocal<Frame?> _current = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<object, object> _stores = new(ReferenceEqualityComparer.Instance);
    private readonly ILogger _logger;

    /// <summary>Creates the database.</summary>
    /// <param name="dialect">The SQL dialect.</param>
    /// <param name="openConnection">Opens a connection (from a pool).</param>
    /// <param name="schema">The schema tables live in; the connection's default when omitted.</param>
    /// <param name="logger">Receives after-commit failures and rollback problems.</param>
    public Database(ISqlDialect dialect, Func<CancellationToken, ValueTask<DbConnection>> openConnection, string? schema = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(openConnection);
        Dialect = dialect;
        _open = openConnection;
        Schema = schema;
        _logger = logger ?? NullLogger.Instance;
    }

    public ISqlDialect Dialect { get; }

    public string? Schema { get; }

    /// <summary>Whether the caller runs inside a transaction of this database.</summary>
    public bool InTransaction => _current.Value is { Active: true };

    /// <summary>A writer for a statement of this database.</summary>
    public SqlWriter NewWriter() => new(Dialect, Schema);

    // :: ITransactionService

    public Task<T> Execute<T>(Propagation propagation, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var current = _current.Value is { Active: true } frame ? frame : null;
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
        if (_current.Value is { Active: true } frame)
        {
            frame.Root.AfterCommit.Add(task);
            return Task.CompletedTask;
        }

        return RunAfterCommit([task]);
    }

    // :: Repositories by mapping (a cascade writes each child through its own repository)

    internal void Register<TEntity>(EntityMapping<TEntity> mapping, IEntityStore<TEntity> store)
        where TEntity : class => _stores[mapping] = store;

    internal IEntityStore<TEntity> StoreOf<TEntity>(EntityMapping<TEntity> mapping)
        where TEntity : class =>
        _stores.TryGetValue(mapping, out var store)
            ? (IEntityStore<TEntity>)store
            : throw new InvalidOperationException(
                $"No repository of {mapping.Model.Name} exists on this database: create it before writing {mapping.Model.Name} in cascade.");

    // :: Detached transactions

    public async Task<IDetachedTransaction> BeginDetached(CancellationToken cancellationToken = default)
    {
        var (connection, transaction) = await Open(cancellationToken).ConfigureAwait(false);
        return new Detached(this, new Frame(connection, transaction));
    }

    // :: Statements

    /// <summary>
    /// Runs a statement that changes data or schema (DDL, a bulk update), inside the open transaction or one of its
    /// own. Returns the number of rows affected.
    /// </summary>
    public Task<int> Execute(SqlStatement statement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return Write(async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, statement);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Runs a query and returns the first column of the first row, or <see langword="null"/>.</summary>
    public Task<object?> Scalar(SqlStatement statement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return Read(async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, statement);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is DBNull ? null : value;
        }, cancellationToken);
    }

    /// <summary>Runs <paramref name="work"/> on the transaction's connection, or on a connection of its own.</summary>
    internal async Task<T> Read<T>(Func<DbConnection, DbTransaction?, Task<T>> work, CancellationToken cancellationToken)
    {
        if (_current.Value is { Active: true } frame)
        {
            return await work(frame.Connection, frame.Transaction).ConfigureAwait(false);
        }

        await using var connection = await _open(cancellationToken).ConfigureAwait(false);
        return await work(connection, null).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="work"/> inside the open transaction, or inside one of its own (implicit REQUIRED).</summary>
    internal Task<T> Write<T>(Func<DbConnection, DbTransaction, Task<T>> work, CancellationToken cancellationToken) =>
        Execute(Propagation.Required, _ =>
        {
            var frame = _current.Value!;
            return work(frame.Connection, frame.Transaction!);
        }, cancellationToken);

    internal static DbCommand Command(DbConnection connection, DbTransaction? transaction, SqlStatement statement)
    {
        var command = connection.CreateCommand();
        command.CommandText = statement.Text;
        command.Transaction = transaction;
        for (var i = 0; i < statement.Parameters.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            switch (statement.Parameters[i])
            {
                case TypedNull typed:
                    parameter.DbType = DbTypeOf(typed.ClrType);
                    parameter.Value = DBNull.Value;
                    break;
                case var value:
                    parameter.Value = value ?? DBNull.Value;
                    break;
            }

            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static System.Data.DbType DbTypeOf(Type type) => type switch
    {
        _ when type == typeof(byte[]) => System.Data.DbType.Binary,
        _ when type == typeof(long) => System.Data.DbType.Int64,
        _ when type == typeof(int) => System.Data.DbType.Int32,
        _ when type == typeof(double) => System.Data.DbType.Double,
        _ when type == typeof(decimal) => System.Data.DbType.Decimal,
        _ when type == typeof(bool) => System.Data.DbType.Boolean,
        _ when type == typeof(DateTimeOffset) => System.Data.DbType.DateTimeOffset,
        _ when type == typeof(DateOnly) => System.Data.DbType.Date,
        _ when type == typeof(Guid) => System.Data.DbType.Guid,
        _ => System.Data.DbType.String,
    };

    // :: Internals

    private async Task<(DbConnection Connection, DbTransaction Transaction)> Open(CancellationToken cancellationToken)
    {
        DbConnection connection;
        try
        {
            connection = await _open(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception caught) when (caught is not OperationCanceledException)
        {
            throw new TransactionSystemException("Could not open a connection for the transaction.", caught);
        }

        try
        {
            return (connection, await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception caught)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw caught is OperationCanceledException
                ? caught
                : new TransactionSystemException("Could not begin the transaction.", caught);
        }
    }

    private async Task<T> RunNew<T>(Func<ITransactionContext, Task<T>> work, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await Open(cancellationToken).ConfigureAwait(false);
        var frame = new Frame(connection, transaction);
        _current.Value = frame; // scoped to this async flow: the caller's value is back when we return
        T result;
        var committed = false;
        try
        {
            try
            {
                result = await work(new Context(frame)).ConfigureAwait(false);
            }
            catch
            {
                await SafeRollback(transaction).ConfigureAwait(false);
                throw; // the work's exception, unchanged
            }

            if (frame.RollbackOnly)
            {
                await SafeRollback(transaction).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                    committed = true;
                }
                catch (Exception caught)
                {
                    await SafeRollback(transaction).ConfigureAwait(false);
                    throw new TransactionSystemException("Could not commit the transaction.", caught);
                }
            }
        }
        finally
        {
            frame.Active = false;
            _current.Value = null;
            await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        if (committed && frame.AfterCommit.Count > 0)
        {
            await RunAfterCommit(frame.AfterCommit).ConfigureAwait(false); // outside the transaction, connection returned
        }

        return result;
    }

    private async Task<T> RunWithout<T>(Func<ITransactionContext, Task<T>> work)
    {
        _current.Value = null; // suspends the caller's transaction for this flow only
        return await work(Context.Inactive).ConfigureAwait(false);
    }

    private async Task RunAfterCommit(IEnumerable<Func<Task>> tasks)
    {
        foreach (var task in tasks)
        {
            try
            {
                await task().ConfigureAwait(false);
            }
            catch (Exception caught)
            {
                _logger.LogError(caught, "An after-commit task failed; the transaction stays committed.");
            }
        }
    }

    private async Task SafeRollback(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception caught)
        {
            _logger.LogError(caught, "Rolling back failed.");
        }
    }

    private sealed class Detached(Database database, Frame frame) : IDetachedTransaction
    {
        private int _finished;

        public bool IsRollbackOnly => frame.RollbackOnly;

        public void SetRollbackOnly() => frame.RollbackOnly = true;

        public async Task<T> Run<T>(Func<Task<T>> work)
        {
            ArgumentNullException.ThrowIfNull(work);
            if (!frame.Active)
            {
                throw new TransactionConflictException("The transaction is already finished.");
            }

            database._current.Value = frame; // this flow only: the caller's value is back when we return
            return await work().ConfigureAwait(false);
        }

        public async Task<bool> Commit(CancellationToken cancellationToken = default)
        {
            if (!Finish())
            {
                throw new TransactionConflictException("The transaction is already finished.");
            }

            var committed = false;
            try
            {
                if (frame.RollbackOnly)
                {
                    await database.SafeRollback(frame.Transaction!).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        await frame.Transaction!.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                        committed = true;
                    }
                    catch (Exception caught)
                    {
                        await database.SafeRollback(frame.Transaction!).ConfigureAwait(false);
                        throw new TransactionSystemException("Could not commit the transaction.", caught);
                    }
                }
            }
            finally
            {
                await Release().ConfigureAwait(false);
            }

            if (committed && frame.AfterCommit.Count > 0)
            {
                await database.RunAfterCommit(frame.AfterCommit).ConfigureAwait(false);
            }

            return committed;
        }

        public async Task Rollback(CancellationToken cancellationToken = default)
        {
            if (!Finish())
            {
                return;
            }

            try
            {
                await database.SafeRollback(frame.Transaction!).ConfigureAwait(false);
            }
            finally
            {
                await Release().ConfigureAwait(false);
            }
        }

        public ValueTask DisposeAsync() => new(Rollback());

        private bool Finish()
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0)
            {
                return false;
            }

            frame.Active = false;
            return true;
        }

        private async Task Release()
        {
            await frame.Transaction!.DisposeAsync().ConfigureAwait(false);
            await frame.Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class Frame(DbConnection connection, DbTransaction transaction)
    {
        public DbConnection Connection { get; } = connection;

        public DbTransaction? Transaction { get; } = transaction;

        public bool Active { get; set; } = true;

        public bool RollbackOnly { get; set; }

        public List<Func<Task>> AfterCommit { get; } = [];

        public Frame Root => this;
    }

    private sealed class Context : ITransactionContext
    {
        private readonly Frame? _frame;

        public Context(Frame frame) => _frame = frame;

        private Context() => _frame = null;

        public static Context Inactive { get; } = new();

        public bool IsActive => _frame is { Active: true };

        public bool IsRollbackOnly => _frame?.RollbackOnly ?? false;

        public void SetRollbackOnly()
        {
            if (_frame is null)
            {
                throw new TransactionSystemException("There is no transaction to roll back.");
            }

            _frame.RollbackOnly = true;
        }
    }
}
