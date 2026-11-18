namespace Shopping.Presentation.Tests;

public sealed class HomeTests(ShoppingDatabaseFixture db) : HomeTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
