namespace Wdc.Persistence.Tests;

public sealed class PostgreSqlRepositoryTests(PostgreSqlFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<PostgreSqlFixture>;
