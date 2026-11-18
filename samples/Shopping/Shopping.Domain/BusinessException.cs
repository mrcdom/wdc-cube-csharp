namespace Shopping.Domain;

/// <summary>A rule of the business was broken; the message is meant for the user.</summary>
public class BusinessException : Exception
{
    public BusinessException()
    {
    }

    public BusinessException(string message)
        : base(message)
    {
    }

    public BusinessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
