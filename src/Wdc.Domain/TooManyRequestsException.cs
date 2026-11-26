namespace Wdc.Domain;

/// <summary>
/// Refused for now: too many requests, or too much open at once. REST maps it to 429, with <c>Retry-After</c> when
/// <see cref="RetryAfter"/> is known.
/// </summary>
public class TooManyRequestsException : Exception
{
    public TooManyRequestsException()
    {
    }

    public TooManyRequestsException(string message)
        : base(message)
    {
    }

    public TooManyRequestsException(string message, TimeSpan? retryAfter)
        : base(message)
    {
        RetryAfter = retryAfter;
    }

    public TooManyRequestsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>How long to wait before trying again, when known.</summary>
    public TimeSpan? RetryAfter { get; }
}
