namespace Wdc.Domain;

/// <summary>A request that cannot be honoured as asked (unknown ordering, delete without a filter). REST maps it to 400.</summary>
public class InvalidRequestException : Exception
{
    public InvalidRequestException()
    {
    }

    public InvalidRequestException(string message)
        : base(message)
    {
    }

    public InvalidRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
