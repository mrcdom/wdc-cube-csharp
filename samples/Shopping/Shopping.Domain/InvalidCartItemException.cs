namespace Shopping.Domain;

/// <summary>A cart line cannot be bought (quantity below one).</summary>
public sealed class InvalidCartItemException : BusinessException
{
    public InvalidCartItemException()
    {
    }

    public InvalidCartItemException(string message)
        : base(message)
    {
    }

    public InvalidCartItemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
