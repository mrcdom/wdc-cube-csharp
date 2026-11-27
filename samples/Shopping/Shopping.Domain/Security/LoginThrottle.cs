using System.Security.Cryptography;
using System.Text;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// Slows down password guessing per user name, across instances: after <see cref="FreeFailures"/> failures in a
/// row, each new attempt waits <see cref="FirstWait"/>, doubled at each further failure up to <see cref="MaxWait"/>.
/// A success clears the count; so does <see cref="ForgetAfter"/> without failures.
/// </summary>
/// <remarks>Counts by user name whether it exists or not, so the answer does not tell who has an account.</remarks>
public sealed class LoginThrottle(ISecurityStore store, TimeProvider time)
{
    public const string Refused = "Too many failed logins: try again later.";

    public int FreeFailures { get; init; } = 5;

    public TimeSpan FirstWait { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan ForgetAfter { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Before checking the password.</summary>
    /// <exception cref="TooManyRequestsException">The user name must still wait (with how long).</exception>
    public async Task Check(string userName, CancellationToken cancellationToken = default)
    {
        if (await store.FailuresOf(Key(userName), cancellationToken) is not { } failures)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (failures.Last >= now - ForgetAfter && WaitAfter(failures.Count) is { } wait && failures.Last + wait > now)
        {
            throw new TooManyRequestsException(Refused, failures.Last + wait - now);
        }
    }

    public Task Failed(string userName, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        return store.RecordFailure(Key(userName), now, now - ForgetAfter, cancellationToken);
    }

    public Task Succeeded(string userName, CancellationToken cancellationToken = default) =>
        store.ClearFailures(Key(userName), cancellationToken);

    /// <summary>How long a user name waits after <paramref name="failures"/> in a row; none while they are free.</summary>
    public TimeSpan? WaitAfter(int failures)
    {
        var over = failures - FreeFailures;
        if (over <= 0)
        {
            return null;
        }

        var wait = FirstWait * Math.Pow(2, Math.Min(over - 1, 20));
        return wait < MaxWait ? wait : MaxWait;
    }

    /// <summary>The user name as counted: trimmed, lower case; a very long one by its hash.</summary>
    public static string Key(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        var key = userName.Trim().ToLowerInvariant();
        return key.Length <= 200 ? key : "#" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
}
