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

/// <summary>The caller is authenticated but may not do this. REST maps it to 403.</summary>
public class AccessDeniedException : Exception
{
    public AccessDeniedException()
    {
    }

    public AccessDeniedException(string message)
        : base(message)
    {
    }

    public AccessDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
