namespace Wdc.Domain;

/// <summary>The transaction infrastructure failed (begin, commit, rollback), as opposed to the work itself.</summary>
public class TransactionSystemException : Exception
{
    public TransactionSystemException()
    {
    }

    public TransactionSystemException(string message)
        : base(message)
    {
    }

    public TransactionSystemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
