using Shopping.Domain;
using Shopping.Presentation.Infra;
using Wdc.Rest.Client;

namespace Shopping.Client;

/// <summary>
/// The Shopping repositories, transactions and login over HTTP. The presentation receives the same
/// <see cref="ShoppingRepositories"/> and <see cref="IAuthentication"/> it gets locally.
/// </summary>
public sealed class ShoppingClient
{
    private readonly Credentials _credentials;

    /// <param name="http">With <see cref="HttpClient.BaseAddress"/> set to the Shopping server.</param>
    public ShoppingClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _credentials = new Credentials();
        var options = new RestClientOptions
        {
            AccessToken = _ => ValueTask.FromResult(_credentials.AccessToken),
            RefreshAccess = ct => new ValueTask<bool>(Authentication!.RefreshAccess(ct)), // set just below
        };
        Rest = new RestClient(http, options);
        Authentication = new RemoteAuthentication(http, Rest, _credentials);
        Repositories = new ShoppingRepositories(
            new RemoteUserRepository(Rest),
            new RemoteProductRepository(Rest),
            new RemotePurchaseRepository(Rest),
            new RemotePurchaseItemRepository(Rest),
            new RemoteTransactionService(Rest));
    }

    public RestClient Rest { get; }

    public ShoppingRepositories Repositories { get; }

    public RemoteAuthentication Authentication { get; }

    internal sealed class Credentials
    {
        public string? AccessToken { get; set; }

        public string? RefreshToken { get; set; }
    }
}
