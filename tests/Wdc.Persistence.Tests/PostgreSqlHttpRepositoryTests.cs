namespace Wdc.Persistence.Tests;

/// <summary>The same contract through the REST routes: remote repositories and transactions.</summary>
public sealed class PostgreSqlHttpRepositoryTests(PostgreSqlHttpFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<PostgreSqlHttpFixture>;
