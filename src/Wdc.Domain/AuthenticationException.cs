namespace Wdc.Domain;

/// <summary>The caller is not authenticated (no session, or it expired). REST maps it to 401.</summary>
public class AuthenticationException : Exception
{
    public AuthenticationException()
    {
    }

    public AuthenticationException(string message)
        : base(message)
    {
    }

    public AuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
