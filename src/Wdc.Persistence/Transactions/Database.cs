using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>
/// A database: its dialect, how to open a connection, and the ambient transaction (decision D-09).
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
public sealed class Database : ITransactionService
{
    private readonly Func<CancellationToken, ValueTask<DbConnection>> _open;
    private readonly AsyncLocal<Frame?> _current = new();
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

    // :: Statements

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

    private async Task<T> RunNew<T>(Func<ITransactionContext, Task<T>> work, CancellationToken cancellationToken)
    {
        DbConnection connection;
        DbTransaction transaction;
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
            transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception caught)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw caught is OperationCanceledException
                ? caught
                : new TransactionSystemException("Could not begin the transaction.", caught);
        }

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
