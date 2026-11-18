namespace Wdc.Domain;

/// <summary>
/// A transaction in a state that does not allow the request: unknown, expired, already finished, or a write without
/// the transaction its owner has open. REST maps it to 409.
/// </summary>
public class TransactionConflictException : TransactionSystemException
{
    public TransactionConflictException()
    {
    }

    public TransactionConflictException(string message)
        : base(message)
    {
    }

    public TransactionConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
