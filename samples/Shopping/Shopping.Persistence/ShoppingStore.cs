using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shopping.Domain;
using Shopping.Domain.Security;
using Wdc.Persistence;
using Wdc.Persistence.PostgreSql;
using Wdc.Persistence.SqlServer;

namespace Shopping.Persistence;

/// <summary>
/// The Shopping database: picks the dialect, creates the schema and the demo data when the tables are missing, and
/// hands out the repositories.
/// </summary>
public sealed class ShoppingStore : IAsyncDisposable
{
    private readonly IAsyncDisposable? _owned;

    private ShoppingStore(DatabaseProvider provider, Database database, IAsyncDisposable? owned,
        Func<CancellationToken, Task<DbConnection>> open)
    {
        Provider = provider;
        Database = database;
        _owned = owned;
        Security = new SecurityStore(provider, database, database.Schema!, open);
        var items = new PurchaseItemRepository(database);
        Repositories = new ShoppingRepositories(
            new UserRepository(database),
            new ProductRepository(database),
            new PurchaseRepository(database),
            items,
            new PurchaseAmendmentRepository(database),
            new PurchaseAmendmentLineRepository(database),
            database);
    }

    public DatabaseProvider Provider { get; }

    public Database Database { get; }

    public ShoppingRepositories Repositories { get; }

    /// <summary>The login's challenges, refresh tokens and failures, and the data protection keys.</summary>
    public ISecurityStore Security { get; }

    public static ShoppingStore Open(DatabaseProvider provider, string connectionString, string schema, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        switch (provider)
        {
            case DatabaseProvider.PostgreSql:
                var dataSource = NpgsqlDataSource.Create(connectionString);
                return new ShoppingStore(provider, PostgreSql.CreateDatabase(dataSource, schema, logger), dataSource,
                    async ct => await dataSource.OpenConnectionAsync(ct));
            case DatabaseProvider.SqlServer:
                return new ShoppingStore(provider, SqlServer.CreateDatabase(connectionString, schema, logger), null, async ct =>
                {
                    var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync(ct);
                    return connection;
                });
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    /// <summary>
    /// Creates the schema, the tables and the demo data, unless the tables already exist; the login's tables are
    /// added to a database made before them.
    /// </summary>
    /// <returns>Whether the schema and the demo data were created.</returns>
    public async Task<bool> EnsureCreated(CancellationToken cancellationToken = default)
    {
        var schema = Database.Schema!;
        var exists = await Database.Scalar(Statement(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @p0 AND table_name = 'product'", schema),
            cancellationToken);
        var created = Convert.ToInt64(exists, System.Globalization.CultureInfo.InvariantCulture) == 0;
        if (created)
        {
            foreach (var ddl in ShoppingSchema.Create(Provider, schema))
            {
                await Database.Execute(Statement(ddl), cancellationToken);
            }
        }

        foreach (var ddl in ShoppingSchema.Security(Provider, schema))
        {
            await Database.Execute(Statement(ddl), cancellationToken);
        }

        if (created)
        {
            await ShoppingDemoData.Load(Repositories, cancellationToken);
        }

        return created;
    }

    /// <summary>Drops every table and the schema (tests).</summary>
    public async Task Drop(CancellationToken cancellationToken = default)
    {
        foreach (var ddl in ShoppingSchema.Drop(Provider, Database.Schema!))
        {
            await Database.Execute(Statement(ddl), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_owned is not null)
        {
            await _owned.DisposeAsync();
        }

        if (Provider == DatabaseProvider.SqlServer)
        {
            SqlConnection.ClearAllPools();
        }
    }

    private static SqlStatement Statement(string sql, params object?[] parameters) => new(sql, parameters);
}

/// <summary>The DDL of each database. Indexes back each named ordering.</summary>
internal static class ShoppingSchema
{
    private static readonly string[] s_tables = ["data_protection_key", "login_failure", "refresh_token", "auth_nonce", "purchase_amendment_line", "purchase_amendment", "purchase_item", "purchase", "user_account", "product"];

    public static IEnumerable<string> Create(DatabaseProvider provider, string schema) =>
        provider == DatabaseProvider.PostgreSql ? PostgreSql(schema) : SqlServer(schema);

    /// <summary>The login's tables, each created only when missing.</summary>
    public static IEnumerable<string> Security(DatabaseProvider provider, string s) =>
        provider == DatabaseProvider.PostgreSql
            ? [
                $"""
                CREATE TABLE IF NOT EXISTS "{s}".auth_nonce (
                    nonce varchar(64) PRIMARY KEY,
                    user_name varchar(255) NOT NULL,
                    expires_at timestamptz NOT NULL)
                """,
                $"""
                CREATE TABLE IF NOT EXISTS "{s}".refresh_token (
                    token_hash varchar(64) PRIMARY KEY,
                    user_id bigint NOT NULL REFERENCES "{s}".user_account (id) ON DELETE CASCADE,
                    expires_at timestamptz NOT NULL)
                """,
                $"""
                CREATE TABLE IF NOT EXISTS "{s}".login_failure (
                    user_key varchar(255) PRIMARY KEY,
                    failures int NOT NULL,
                    last_failure timestamptz NOT NULL)
                """,
                $"""
                CREATE TABLE IF NOT EXISTS "{s}".data_protection_key (
                    id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    name varchar(255) NOT NULL,
                    xml text NOT NULL)
                """,
            ]
            : [
                $"""
                IF OBJECT_ID(N'[{s}].[auth_nonce]', N'U') IS NULL CREATE TABLE [{s}].[auth_nonce] (
                    nonce nvarchar(64) PRIMARY KEY,
                    user_name nvarchar(255) NOT NULL,
                    expires_at datetimeoffset NOT NULL)
                """,
                $"""
                IF OBJECT_ID(N'[{s}].[refresh_token]', N'U') IS NULL CREATE TABLE [{s}].[refresh_token] (
                    token_hash nvarchar(64) PRIMARY KEY,
                    user_id bigint NOT NULL REFERENCES [{s}].[user_account] (id) ON DELETE CASCADE,
                    expires_at datetimeoffset NOT NULL)
                """,
                $"""
                IF OBJECT_ID(N'[{s}].[login_failure]', N'U') IS NULL CREATE TABLE [{s}].[login_failure] (
                    user_key nvarchar(255) PRIMARY KEY,
                    failures int NOT NULL,
                    last_failure datetimeoffset NOT NULL)
                """,
                $"""
                IF OBJECT_ID(N'[{s}].[data_protection_key]', N'U') IS NULL CREATE TABLE [{s}].[data_protection_key] (
                    id bigint IDENTITY(1, 1) PRIMARY KEY,
                    name nvarchar(255) NOT NULL,
                    xml nvarchar(max) NOT NULL)
                """,
            ];

    public static IEnumerable<string> Drop(DatabaseProvider provider, string schema) =>
        provider == DatabaseProvider.PostgreSql
            ? [$"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE"]
            : [.. s_tables.Select(t => $"DROP TABLE IF EXISTS [{schema}].[{t}]"), $"DROP SCHEMA IF EXISTS [{schema}]"];

    private static IEnumerable<string> PostgreSql(string s)
    {
        yield return $"CREATE SCHEMA IF NOT EXISTS \"{s}\"";
        yield return $"""
            CREATE TABLE "{s}".product (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                name text NOT NULL,
                price numeric(20, 2) NOT NULL,
                description text NOT NULL,
                image bytea)
            """;
        yield return $"CREATE INDEX ix_product_name ON \"{s}\".product (name)";
        yield return $"CREATE INDEX ix_product_price ON \"{s}\".product (price)";
        yield return $"""
            CREATE TABLE "{s}".user_account (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                user_name varchar(255) NOT NULL UNIQUE,
                password varchar(255) NOT NULL,
                name varchar(255) NOT NULL,
                roles varchar(255))
            """;
        yield return $"CREATE INDEX ix_user_name ON \"{s}\".user_account (name)";
        yield return $"""
            CREATE TABLE "{s}".purchase (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                user_id bigint NOT NULL REFERENCES "{s}".user_account (id),
                buy_date timestamptz NOT NULL)
            """;
        yield return $"CREATE INDEX ix_purchase_recent ON \"{s}\".purchase (user_id, buy_date DESC, id DESC)";
        yield return $"""
            CREATE TABLE "{s}".purchase_item (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                purchase_id bigint NOT NULL REFERENCES "{s}".purchase (id),
                product_id bigint NOT NULL REFERENCES "{s}".product (id),
                amount integer NOT NULL CHECK (amount > 0),
                price numeric(20, 2) NOT NULL)
            """;
        yield return $"CREATE INDEX ix_purchase_item_purchase ON \"{s}\".purchase_item (purchase_id)";
        yield return $"""
            CREATE TABLE "{s}".purchase_amendment (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                purchase_id bigint NOT NULL REFERENCES "{s}".purchase (id),
                author_id bigint NOT NULL REFERENCES "{s}".user_account (id),
                amend_date timestamptz NOT NULL,
                reason text NOT NULL)
            """;
        yield return $"CREATE INDEX ix_purchase_amendment_purchase ON \"{s}\".purchase_amendment (purchase_id)";
        yield return $"""
            CREATE TABLE "{s}".purchase_amendment_line (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                amendment_id bigint NOT NULL REFERENCES "{s}".purchase_amendment (id),
                item_id bigint NOT NULL,
                product_id bigint NOT NULL REFERENCES "{s}".product (id),
                price numeric(20, 2) NOT NULL,
                amount_before integer NOT NULL,
                amount_after integer NOT NULL,
                CHECK (amount_after >= 0 AND amount_after < amount_before))
            """;
        yield return $"CREATE INDEX ix_purchase_amendment_line_amendment ON \"{s}\".purchase_amendment_line (amendment_id)";
    }

    private static IEnumerable<string> SqlServer(string s)
    {
        yield return $"IF SCHEMA_ID(N'{s}') IS NULL EXEC(N'CREATE SCHEMA [{s}]')";
        yield return $"""
            CREATE TABLE [{s}].[product] (
                id bigint IDENTITY(1, 1) PRIMARY KEY,
                name nvarchar(1000) NOT NULL,
                price decimal(20, 2) NOT NULL,
                description nvarchar(max) NOT NULL,
                image varbinary(max) NULL)
            """;
        yield return $"CREATE INDEX ix_product_name ON [{s}].[product] (name)";
        yield return $"CREATE INDEX ix_product_price ON [{s}].[product] (price)";
        yield return $"""
            CREATE TABLE [{s}].[user_account] (
                id bigint IDENTITY(1, 1) PRIMARY KEY,
                user_name nvarchar(255) NOT NULL UNIQUE,
                password nvarchar(255) NOT NULL,
                name nvarchar(255) NOT NULL,
                roles nvarchar(255) NULL)
            """;
        yield return $"CREATE INDEX ix_user_name ON [{s}].[user_account] (name)";
        yield return $"""
            CREATE TABLE [{s}].[purchase] (
                id bigint IDENTITY(1, 1) PRIMARY KEY,
                user_id bigint NOT NULL REFERENCES [{s}].[user_account] (id),
                buy_date datetimeoffset NOT NULL)
            """;
        yield return $"CREATE INDEX ix_purchase_recent ON [{s}].[purchase] (user_id, buy_date DESC, id DESC)";
        yield return $"""
            CREATE TABLE [{s}].[purchase_item] (
                id bigint IDENTITY(1, 1) PRIMARY KEY,
                purchase_id bigint NOT NULL REFERENCES [{s}].[purchase] (id),
                product_id bigint NOT NULL REFERENCES [{s}].[product] (id),
                amount int NOT NULL CHECK (amount > 0),
                price decimal(20, 2) NOT NULL)
            """;
        yield return $"CREATE INDEX ix_purchase_item_purchase ON [{s}].[purchase_item] (purchase_id)";
        yield return $"""
            CREATE TABLE [{s}].[purchase_amendment] (
                id bigint IDENTITY(1, 1) PRIMARY KEY,
                purchase_id bigint NOT NULL REFERENCES [{s}].[purchase] (id),
                author_id bigint NOT NULL REFERENCES [{s}].[user_account] (id),
                amend_date datetimeoffset NOT NULL,
                reason nvarchar(max) NOT NULL)
            """;
        yield return $"CREATE INDEX ix_purchase_amendment_purchase ON [{s}].[purchase_amendment] (purchase_id)";
        yield return $"""
            CREATE TABLE [{s}].[purchase_amendment_line] (
                id bigint IDENTITY(1, 1) PRIMARY KEY,
                amendment_id bigint NOT NULL REFERENCES [{s}].[purchase_amendment] (id),
                item_id bigint NOT NULL,
                product_id bigint NOT NULL REFERENCES [{s}].[product] (id),
                price decimal(20, 2) NOT NULL,
                amount_before int NOT NULL,
                amount_after int NOT NULL,
                CHECK (amount_after >= 0 AND amount_after < amount_before))
            """;
        yield return $"CREATE INDEX ix_purchase_amendment_line_amendment ON [{s}].[purchase_amendment_line] (amendment_id)";
    }
}
