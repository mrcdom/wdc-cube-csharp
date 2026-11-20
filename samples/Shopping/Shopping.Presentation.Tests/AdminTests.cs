namespace Shopping.Presentation.Tests;

public sealed class AdminTests(ShoppingDatabaseFixture db) : AdminTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
