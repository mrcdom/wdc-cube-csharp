namespace Shopping.Presentation.Tests;

/// <summary>The same rules through the Shopping server, with the JWT login.</summary>
public sealed class SecurityHttpTests(ShoppingHttpFixture db) : SecurityTestsBase(db), IClassFixture<ShoppingHttpFixture>;
