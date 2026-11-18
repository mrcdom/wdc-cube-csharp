namespace Shopping.Presentation.Tests;

public sealed class SealedAddressTests(ShoppingDatabaseFixture db) : SealedAddressTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
