namespace Shopping.Presentation.Tests;

/// <summary>The login's tables on PostgreSQL.</summary>
public sealed class SecurityStoreTests(ShoppingDatabaseFixture db) : SecurityStoreTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
