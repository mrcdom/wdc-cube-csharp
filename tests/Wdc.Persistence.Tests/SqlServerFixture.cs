using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Wdc.Persistence.Tests;

public class SqlServerFixture() : DatabaseFixture("WDC_TEST_SQLSERVER")
{
    public override async Task<bool> ReadsDoNotWaitForWriters()
    {
        await using var connection = await OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()";
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override Task<Database> CreateDatabase() =>
        Task.FromResult(SqlServer.SqlServer.CreateDatabase(ConnectionString!, Schema));

    protected override Task DisposeDatabase()
    {
        SqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    protected override async Task<DbConnection> OpenConnection()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    protected override string Qualified(string table) => $"[{Schema}].[{table}]";

    protected override IEnumerable<string> CreateSchemaScript() =>
    [
        $"CREATE SCHEMA [{Schema}]",
        $"""
         CREATE TABLE {Qualified("product")} (
             id bigint IDENTITY(1, 1) PRIMARY KEY,
             name nvarchar(200) NOT NULL,
             price decimal(12, 2) NULL,
             description nvarchar(max) NULL,
             image varbinary(max) NULL)
         """,
        $"""
         CREATE TABLE {Qualified("user_account")} (
             id bigint IDENTITY(1, 1) PRIMARY KEY,
             user_name nvarchar(100) NOT NULL UNIQUE,
             password nvarchar(100) NULL,
             name nvarchar(200) NULL,
             role nvarchar(20) NOT NULL DEFAULT 'Customer')
         """,
        $"""
         CREATE TABLE {Qualified("purchase")} (
             id bigint IDENTITY(1, 1) PRIMARY KEY,
             buy_date datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(),
             user_id bigint NULL REFERENCES {Qualified("user_account")} (id))
         """,
        $"""
         CREATE TABLE {Qualified("purchase_item")} (
             id bigint IDENTITY(1, 1) PRIMARY KEY,
             purchase_id bigint NOT NULL REFERENCES {Qualified("purchase")} (id),
             product_id bigint NULL REFERENCES {Qualified("product")} (id),
             amount int NOT NULL,
             price decimal(12, 2) NOT NULL)
         """,
    ];

    // No CASCADE in SQL Server: tables first (children first), then the schema.
    protected override IEnumerable<string> DropSchemaScript() =>
    [
        .. new[] { "purchase_item", "purchase", "user_account", "product" }.Select(t => $"DROP TABLE IF EXISTS {Qualified(t)}"),
        $"DROP SCHEMA [{Schema}]",
    ];
}
