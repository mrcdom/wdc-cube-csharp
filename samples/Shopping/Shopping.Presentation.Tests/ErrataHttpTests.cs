namespace Shopping.Presentation.Tests;

/// <summary>The same errata through the Shopping server, with the JWT login.</summary>
public sealed class ErrataHttpTests(ShoppingHttpFixture db) : ErrataTestsBase(db), IClassFixture<ShoppingHttpFixture>;
