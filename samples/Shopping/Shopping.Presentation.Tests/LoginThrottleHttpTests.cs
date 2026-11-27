namespace Shopping.Presentation.Tests;

/// <summary>The same through the Shopping server: a 429 with <c>Retry-After</c>.</summary>
public sealed class LoginThrottleHttpTests(ShoppingHttpFixture db) : LoginThrottleTestsBase(db), IClassFixture<ShoppingHttpFixture>;
