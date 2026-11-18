namespace Shopping.Presentation.Tests;

public sealed class BuyingTests(ShoppingDatabaseFixture db) : BuyingTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
