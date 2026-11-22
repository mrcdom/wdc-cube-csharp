namespace Wdc.Domain;

/// <summary>A transaction that does not exist (any more): unknown, expired or already finished. REST maps it to 410.</summary>
public class TransactionNotFoundException : TransactionSystemException
{
    public TransactionNotFoundException()
    {
    }

    public TransactionNotFoundException(string message)
        : base(message)
    {
    }

    public TransactionNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
