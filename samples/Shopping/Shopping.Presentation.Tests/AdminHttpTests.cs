namespace Shopping.Presentation.Tests;

/// <summary>The same, with the repositories and the login through the Shopping server.</summary>
public sealed class AdminHttpTests(ShoppingHttpFixture db) : AdminTestsBase(db), IClassFixture<ShoppingHttpFixture>;
