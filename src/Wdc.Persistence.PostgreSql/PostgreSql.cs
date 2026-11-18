using Microsoft.Extensions.Logging;
using Npgsql;

namespace Wdc.Persistence.PostgreSql;

/// <summary>Creates a <see cref="Database"/> over PostgreSQL.</summary>
public static class PostgreSql
{
    /// <summary>A database over a pooled data source the caller owns (and disposes).</summary>
    public static Database CreateDatabase(NpgsqlDataSource dataSource, string? schema = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        return new Database(PostgreSqlDialect.Instance,
            async cancellationToken => await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false),
            schema, logger);
    }
}
