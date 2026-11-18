using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Wdc.Persistence.SqlServer;

/// <summary>Creates a <see cref="Database"/> over SQL Server.</summary>
public static class SqlServer
{
    /// <summary>A database whose connections come from SqlClient's pool for <paramref name="connectionString"/>.</summary>
    public static Database CreateDatabase(string connectionString, string? schema = null, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new Database(SqlServerDialect.Instance, async cancellationToken =>
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }, schema, logger);
    }
}
