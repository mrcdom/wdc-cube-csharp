namespace Shopping.Presentation.Tests;

/// <summary>The rules in process, as Blazor Server runs them: the repositories protected for the session's actor.</summary>
public sealed class SecurityTests(ShoppingDatabaseFixture db) : SecurityTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
