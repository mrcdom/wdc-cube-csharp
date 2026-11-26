namespace Shopping.Domain.Security;

/// <summary>
/// What the login keeps between requests, in the database so every instance of the server sees the same: the
/// challenges, the refresh tokens, the failed logins and the keys of the host's data protection.
/// </summary>
/// <remarks>Spending is one statement: of two calls with the same nonce or token, only one gets it.</remarks>
public interface ISecurityStore
{
    Task SaveNonce(string nonce, string userName, DateTimeOffset expires, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends a nonce: <see langword="true"/> for the one call that finds it unexpired and issued to
    /// <paramref name="userName"/>. Any attempt spends it.
    /// </summary>
    Task<bool> SpendNonce(string nonce, string userName, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task SaveRefreshToken(string tokenHash, long userId, DateTimeOffset expires, CancellationToken cancellationToken = default);

    /// <summary>Spends a refresh token: its user for the one call that finds it unexpired, <see langword="null"/> otherwise.</summary>
    Task<long?> SpendRefreshToken(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task RevokeRefreshToken(string tokenHash, CancellationToken cancellationToken = default);

    Task<LoginFailures?> FailuresOf(string userKey, CancellationToken cancellationToken = default);

    /// <summary>One more failure; the count starts over when the last one is older than <paramref name="forgetBefore"/>.</summary>
    Task RecordFailure(string userKey, DateTimeOffset now, DateTimeOffset forgetBefore, CancellationToken cancellationToken = default);

    Task ClearFailures(string userKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes the expired nonces and refresh tokens, and the failures last seen before <paramref name="forgetFailuresBefore"/>.</summary>
    Task Sweep(DateTimeOffset now, DateTimeOffset forgetFailuresBefore, CancellationToken cancellationToken = default);

    /// <summary>The data protection keys (XML), oldest first.</summary>
    Task<IReadOnlyList<string>> Keys(CancellationToken cancellationToken = default);

    Task SaveKey(string name, string xml, CancellationToken cancellationToken = default);
}
