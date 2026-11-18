namespace Wdc.Persistence.Tests;

public sealed class SqlServerRepositoryTests(SqlServerFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<SqlServerFixture>;
