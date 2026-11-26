namespace Shopping.Domain.Security;

/// <summary>The failed logins of a user name in a row, and when the last one was.</summary>
public sealed record LoginFailures(int Count, DateTimeOffset Last);
