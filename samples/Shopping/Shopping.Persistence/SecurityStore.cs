using System.Data.Common;
using System.Globalization;
using Shopping.Domain.Security;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>The login's tables, in the Shopping schema: one statement per spend, so two callers never both win.</summary>
public sealed class SecurityStore : ISecurityStore
{
    private readonly DatabaseProvider _provider;
    private readonly Database _database;
    private readonly Func<CancellationToken, Task<DbConnection>> _open;
    private readonly string _nonces, _refreshTokens, _failures, _keys;

    internal SecurityStore(DatabaseProvider provider, Database database, string schema, Func<CancellationToken, Task<DbConnection>> open)
    {
        _provider = provider;
        _database = database;
        _open = open;
        _nonces = Table(schema, "auth_nonce");
        _refreshTokens = Table(schema, "refresh_token");
        _failures = Table(schema, "login_failure");
        _keys = Table(schema, "data_protection_key");
    }

    public Task SaveNonce(string nonce, string userName, DateTimeOffset expires, CancellationToken cancellationToken = default) =>
        Execute($"INSERT INTO {_nonces} (nonce, user_name, expires_at) VALUES (@p0, @p1, @p2)", cancellationToken,
            nonce, userName, expires);

    public async Task<bool> SpendNonce(string nonce, string userName, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await Execute($"DELETE FROM {_nonces} WHERE nonce = @p0 AND user_name = @p1 AND expires_at > @p2", cancellationToken,
                nonce, userName, now) == 1)
        {
            return true;
        }

        await Execute($"DELETE FROM {_nonces} WHERE nonce = @p0", cancellationToken, nonce); // a wrong try spends it too
        return false;
    }

    public Task SaveRefreshToken(string tokenHash, long userId, DateTimeOffset expires, CancellationToken cancellationToken = default) =>
        Execute($"INSERT INTO {_refreshTokens} (token_hash, user_id, expires_at) VALUES (@p0, @p1, @p2)", cancellationToken,
            tokenHash, userId, expires);

    public async Task<long?> SpendRefreshToken(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var sql = _provider == DatabaseProvider.PostgreSql
            ? $"DELETE FROM {_refreshTokens} WHERE token_hash = @p0 AND expires_at > @p1 RETURNING user_id"
            : $"DELETE FROM {_refreshTokens} OUTPUT DELETED.user_id WHERE token_hash = @p0 AND expires_at > @p1";
        var userId = await _database.Scalar(new SqlStatement(sql, [tokenHash, now]), cancellationToken);
        return userId is null ? null : Convert.ToInt64(userId, CultureInfo.InvariantCulture);
    }

    public Task RevokeRefreshToken(string tokenHash, CancellationToken cancellationToken = default) =>
        Execute($"DELETE FROM {_refreshTokens} WHERE token_hash = @p0", cancellationToken, tokenHash);

    public async Task<LoginFailures?> FailuresOf(string userKey, CancellationToken cancellationToken = default) =>
        (await Rows($"SELECT failures, last_failure FROM {_failures} WHERE user_key = @p0",
            r => new LoginFailures(r.GetInt32(0), r.GetFieldValue<DateTimeOffset>(1)), cancellationToken, userKey)).FirstOrDefault();

    public Task RecordFailure(string userKey, DateTimeOffset now, DateTimeOffset forgetBefore, CancellationToken cancellationToken = default) =>
        Execute(_provider == DatabaseProvider.PostgreSql
            ? $"""
                INSERT INTO {_failures} AS f (user_key, failures, last_failure) VALUES (@p0, 1, @p1)
                ON CONFLICT (user_key) DO UPDATE
                SET failures = CASE WHEN f.last_failure < @p2 THEN 1 ELSE f.failures + 1 END, last_failure = @p1
                """
            : $"""
                MERGE {_failures} WITH (HOLDLOCK) AS f
                USING (SELECT @p0 AS user_key) AS k ON f.user_key = k.user_key
                WHEN MATCHED THEN UPDATE
                    SET failures = CASE WHEN f.last_failure < @p2 THEN 1 ELSE f.failures + 1 END, last_failure = @p1
                WHEN NOT MATCHED THEN INSERT (user_key, failures, last_failure) VALUES (@p0, 1, @p1);
                """, cancellationToken, userKey, now, forgetBefore);

    public Task ClearFailures(string userKey, CancellationToken cancellationToken = default) =>
        Execute($"DELETE FROM {_failures} WHERE user_key = @p0", cancellationToken, userKey);

    public async Task Sweep(DateTimeOffset now, DateTimeOffset forgetFailuresBefore, CancellationToken cancellationToken = default)
    {
        await Execute($"DELETE FROM {_nonces} WHERE expires_at <= @p0", cancellationToken, now);
        await Execute($"DELETE FROM {_refreshTokens} WHERE expires_at <= @p0", cancellationToken, now);
        await Execute($"DELETE FROM {_failures} WHERE last_failure < @p0", cancellationToken, forgetFailuresBefore);
    }

    public async Task<IReadOnlyList<string>> Keys(CancellationToken cancellationToken = default) =>
        await Rows($"SELECT xml FROM {_keys} ORDER BY id", r => r.GetString(0), cancellationToken);

    public Task SaveKey(string name, string xml, CancellationToken cancellationToken = default) =>
        Execute($"INSERT INTO {_keys} (name, xml) VALUES (@p0, @p1)", cancellationToken, name, xml);

    private string Table(string schema, string name) =>
        _provider == DatabaseProvider.PostgreSql ? $"\"{schema}\".{name}" : $"[{schema}].[{name}]";

    private Task<int> Execute(string sql, CancellationToken cancellationToken, params object?[] parameters) =>
        _database.Execute(new SqlStatement(sql, parameters), cancellationToken);

    // Several rows: on a connection of its own (the database's statements return one value at most).
    private async Task<List<T>> Rows<T>(string sql, Func<DbDataReader, T> read, CancellationToken cancellationToken,
        params object?[] parameters)
    {
        await using var connection = await _open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i.ToString(CultureInfo.InvariantCulture);
            parameter.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}
