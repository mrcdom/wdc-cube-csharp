namespace Wdc.Cube;

/// <summary>A navigation that cannot settle, such as presenters that keep redirecting to one another.</summary>
public sealed class CubeNavigationException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public CubeNavigationException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public CubeNavigationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public CubeNavigationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
