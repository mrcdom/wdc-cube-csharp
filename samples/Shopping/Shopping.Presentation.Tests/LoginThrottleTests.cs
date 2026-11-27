namespace Shopping.Presentation.Tests;

/// <summary>The login's limit with the users checked in this process (as Blazor Server does).</summary>
public sealed class LoginThrottleTests(ShoppingDatabaseFixture db) : LoginThrottleTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
