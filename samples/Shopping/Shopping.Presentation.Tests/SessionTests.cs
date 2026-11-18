namespace Shopping.Presentation.Tests;

public sealed class SessionTests(ShoppingDatabaseFixture db) : SessionTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
