namespace Shopping.Presentation.Tests;

/// <summary>The errata in process, as Blazor Server runs it.</summary>
public sealed class ErrataTests(ShoppingDatabaseFixture db) : ErrataTestsBase(db), IClassFixture<ShoppingDatabaseFixture>;
