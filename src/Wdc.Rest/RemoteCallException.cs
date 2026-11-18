using System.Net;

namespace Wdc.Rest;

/// <summary>A server answer that is not part of the contract (5xx, a proxy page).</summary>
public sealed class RemoteCallException : Exception
{
    public RemoteCallException()
    {
    }

    public RemoteCallException(string message)
        : base(message)
    {
    }

    public RemoteCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public RemoteCallException(HttpStatusCode status, string message)
        : base(message) => Status = status;

    public HttpStatusCode? Status { get; }
}
