namespace Shopping.Presentation.Tests;

/// <summary>The login's tables on SQL Server.</summary>
public sealed class SecurityStoreSqlServerTests(ShoppingSqlServerFixture db) : SecurityStoreTestsBase(db), IClassFixture<ShoppingSqlServerFixture>;
