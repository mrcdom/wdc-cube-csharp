namespace Wdc.Persistence.Tests;

/// <summary>The same contract through the REST routes (decision D-17/D-18): remote repositories and transactions.</summary>
public sealed class PostgreSqlHttpRepositoryTests(PostgreSqlHttpFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<PostgreSqlHttpFixture>;
