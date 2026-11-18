namespace Wdc.Rest.Client;

/// <summary>How the client authenticates.</summary>
public sealed class RestClientOptions
{
    /// <summary>The bearer token to send, or <see langword="null"/> for none.</summary>
    public Func<CancellationToken, ValueTask<string?>>? AccessToken { get; set; }

    /// <summary>Called after a 401: renews the token and says whether to repeat the call (once).</summary>
    public Func<CancellationToken, ValueTask<bool>>? RefreshAccess { get; set; }

    /// <summary>This client instance, sent as <c>X-Client-Id</c> (part of a remote transaction's owner).</summary>
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
}
