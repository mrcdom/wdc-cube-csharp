namespace Wdc.Domain;

/// <summary>Too many transactions open (in total or for one owner). REST maps it to 429.</summary>
public class TransactionLimitExceededException : TooManyRequestsException
{
    public TransactionLimitExceededException()
    {
    }

    public TransactionLimitExceededException(string message)
        : base(message)
    {
    }

    public TransactionLimitExceededException(string message, TimeSpan? retryAfter)
        : base(message, retryAfter)
    {
    }

    public TransactionLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
