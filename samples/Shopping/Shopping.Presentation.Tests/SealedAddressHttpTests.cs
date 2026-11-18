namespace Shopping.Presentation.Tests;

/// <summary>The same, with the key coming from the server's login.</summary>
public sealed class SealedAddressHttpTests(ShoppingHttpFixture db) : SealedAddressTestsBase(db), IClassFixture<ShoppingHttpFixture>;
