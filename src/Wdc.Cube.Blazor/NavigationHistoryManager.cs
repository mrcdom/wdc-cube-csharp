using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Wdc.Cube.Blazor;

/// <summary>
/// Keeps the identity of the application's state in the browser's address, relative to the app's base URI
/// (<c>https://host/base/orders/detail?oid=7</c> is the address <c>orders/detail?oid=7</c>). Back, Forward and
/// pasted links reach the application as history changes.
/// </summary>
/// <remarks>
/// The host page must answer every path under the base URI (a catch-all page) and the router must be interactive, so
/// that changing the address does not reload the page.
/// </remarks>
public sealed class NavigationHistoryManager : HistoryManager, IDisposable
{
    private readonly NavigationManager _navigation;

    /// <summary>Starts following <paramref name="navigation"/>.</summary>
    public NavigationHistoryManager(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _navigation = navigation;
        _navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>The current address, plain (decoded when a codec is installed), without the base URI or fragment.</summary>
    public override string Location => DecodeAddress(WireLocation);

    private string WireLocation
    {
        get
        {
            var relative = _navigation.ToBaseRelativePath(_navigation.Uri);
            var hash = relative.IndexOf('#', StringComparison.Ordinal);
            return hash < 0 ? relative : relative[..hash];
        }
    }

    /// <inheritdoc/>
    public override void Update(CubeApplication app, Place place)
    {
        ArgumentNullException.ThrowIfNull(app);
        var plain = app.NewFlipIntent(place).ToString();

        var wire = WireLocation;
        var question = wire.IndexOf('?', StringComparison.Ordinal);
        var wireQuery = question < 0 ? string.Empty : wire[(question + 1)..];
        var wantsEnvelope = Codec is not null && plain.Contains('?', StringComparison.Ordinal);
        if (string.Equals(plain, Location, StringComparison.Ordinal) && wantsEnvelope == IsEnvelope(wireQuery))
        {
            return; // same state, same form: nothing to write (never compare envelopes)
        }

        _navigation.NavigateTo(_navigation.BaseUri + EncodeAddress(plain));
    }

    /// <summary>Stops following the browser's address.</summary>
    public void Dispose() => _navigation.LocationChanged -= OnLocationChanged;

    // Our own NavigateTo arrives here too; the application compares the address with its state and ignores it.
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => NotifyChanges();
}
