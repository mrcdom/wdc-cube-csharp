namespace Wdc.Persistence.Tests;

public sealed class SqlServerHttpRepositoryTests(SqlServerHttpFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<SqlServerHttpFixture>;
