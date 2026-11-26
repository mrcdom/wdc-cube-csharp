namespace Wdc.Persistence.Tests;

public sealed class SqlServerHttpFixture : SqlServerFixture
{
    private ShoppingRestHost? _host;

    public override bool IsRemote => true;

    protected override async Task OnReady()
    {
        _host = await ShoppingRestHost.Start(Shopping!);
        Side = new RemoteShopping(_host.NewClient());
    }

    protected override async Task OnDisposing()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }
}
