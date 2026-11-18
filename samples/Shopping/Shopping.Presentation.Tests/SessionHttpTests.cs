namespace Shopping.Presentation.Tests;

/// <summary>The same, with the repositories and the login through the Shopping server.</summary>
public sealed class SessionHttpTests(ShoppingHttpFixture db) : SessionTestsBase(db), IClassFixture<ShoppingHttpFixture>;
