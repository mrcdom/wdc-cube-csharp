namespace Wdc.Domain;

/// <summary>
/// A transaction in a state that does not allow the request: in use by another request, doomed by a failed request,
/// asked the opposite of how it finished, or a write without the transaction its owner has open. REST maps it to
/// 409.
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
