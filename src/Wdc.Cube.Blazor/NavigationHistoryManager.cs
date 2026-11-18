using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Wdc.Cube.Blazor;

/// <summary>
/// Keeps the identity of the application's state in the browser's address, relative to the app's base URI
/// (<c>https://host/base/orders/detail?oid=7</c> is the address <c>orders/detail?oid=7</c>). Back, Forward and
/// pasted links reach the application as history changes.
/// </summary>
/// <remarks>
/// <para>
/// The host page must answer every path under the base URI (a catch-all page) and the router must be interactive, so
/// that changing the address does not reload the page.
/// </para>
/// <para>
/// In Blazor Server <see cref="NavigationManager.NavigateTo(string, bool, bool)"/> is asynchronous: its
/// <see cref="NavigationManager.Uri"/> changes only when the browser confirms. The addresses written and not confirmed
/// yet are kept here, so two updates in a row write one history entry (not two), and the confirmations of our own
/// writes are not mistaken for the user moving (Back, a pasted link).
/// </para>
/// </remarks>
public sealed class NavigationHistoryManager : HistoryManager, IDisposable
{
    private readonly NavigationManager _navigation;
    private readonly List<string> _pending = [];

    /// <summary>Starts following <paramref name="navigation"/>.</summary>
    public NavigationHistoryManager(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _navigation = navigation;
        _navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>
    /// The current address, plain (decoded when a codec is installed), without the base URI or fragment: the last one
    /// written when the browser has not confirmed it yet.
    /// </summary>
    public override string Location => DecodeAddress(CurrentWire);

    private string CurrentWire => _pending.Count > 0 ? _pending[^1] : Relative(_navigation.Uri);

    /// <inheritdoc/>
    public override void Update(CubeApplication app, Place place)
    {
        ArgumentNullException.ThrowIfNull(app);
        var plain = app.NewFlipIntent(place).ToString();

        var wire = CurrentWire;
        var question = wire.IndexOf('?', StringComparison.Ordinal);
        var wireQuery = question < 0 ? string.Empty : wire[(question + 1)..];
        var wantsEnvelope = Codec is not null && plain.Contains('?', StringComparison.Ordinal);
        if (string.Equals(plain, Location, StringComparison.Ordinal) && wantsEnvelope == IsEnvelope(wireQuery))
        {
            return; // same state, same form: nothing to write (never compare envelopes)
        }

        var target = EncodeAddress(plain);
        _pending.Add(target);
        _navigation.NavigateTo(_navigation.BaseUri + target);
    }

    /// <summary>Stops following the browser's address.</summary>
    public void Dispose() => _navigation.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var wire = Relative(e.Location);
        var index = _pending.IndexOf(wire);
        if (index >= 0)
        {
            _pending.RemoveRange(0, index + 1); // the browser confirmed one of our own writes: nothing moved
            return;
        }

        _pending.Clear(); // the user moved (Back, Forward, a pasted link): the browser is the truth again
        NotifyChanges();
    }

    private string Relative(string uri)
    {
        var relative = _navigation.ToBaseRelativePath(uri);
        var hash = relative.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? relative : relative[..hash];
    }
}
