using System.Net.Mail;
using Tutorial.Presentation.Modules.Main;
using Tutorial.Presentation.Services;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Subscriptions;

/// <summary>A dialog over the list. It owns the address's site id and publishes its scope in the dialog slot.</summary>
public sealed class SubscriptionsDetailPresenter(MainPresenter app)
    : CubePresenter<MainPresenter, SubscriptionsDetailScope>(app, new SubscriptionsDetailScope())
{
    private ScopeSlot _dialogSlot = ScopeSlots.Noop;

    /// <summary>The site this dialog is about. Known from the address before its data arrives.</summary>
    private long? _siteId;

    private SiteItem? _item;

    /// <summary>Bumped on every load: a response that is no longer the latest stays silent.</summary>
    private int _pending;

    private FlipIntent? _previousIntent;

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new SubscriptionsDetailKeys(App, intent);
        _dialogSlot = keys.DialogSlot;
        var arrived = keys.Item;

        if (keys.SiteId is not { } siteId)
        {
            // Redirecting inside our own flip: with the intent in transit.
            await App.FlipToIntent(intent.Redirect(Places.Subscriptions));
            return false;
        }

        if (initialization)
        {
            if (App.HasNavigated)
            {
                _previousIntent = App.NewFlipIntent(App.LastPlace);
            }

            Scope.OnClose = Action(Close);
            Scope.OnSubscribe = Action(OnSubscribe);
            Scope.OnEmailChanged = Mirror<string>(email => Scope.Email = email); // the box shows the scope: keep it in step
        }

        _dialogSlot(Scope);
        if (siteId != _siteId)
        {
            _ = ShowSite(siteId, arrived);
        }

        return true;
    }

    public override void PublishParameters(FlipIntent intent) =>
        new SubscriptionsDetailKeys(App, intent).SiteId = _siteId;

    public override void Release()
    {
        _dialogSlot(null); // without it the dialog stays drawn after the place left the path
        base.Release();
    }

    private async Task ShowSite(long siteId, SiteItem? arrived)
    {
        var token = ++_pending;
        _siteId = siteId;
        _item = null;
        Scope.Site = null;
        Scope.Email = string.Empty;

        SiteItem item;
        try
        {
            item = arrived?.Id == siteId ? arrived : await App.Service.FetchSiteItem(siteId);
        }
        catch (KeyNotFoundException)
        {
            if (token == _pending)
            {
                App.Alert(AlertSeverity.Warning, "Unknown site", $"There is no site with id {siteId} to subscribe to.");
                await Close();
            }

            return;
        }

        if (token != _pending || IsReleased)
        {
            return;
        }

        _item = item;
        Scope.Site = item.Site;
    }

    private async Task OnSubscribe()
    {
        var siteId = _item?.Id ?? throw new InvalidOperationException("The site must be known before subscribing.");
        var email = Scope.Email.Trim();
        if (email.Length == 0)
        {
            App.Alert(AlertSeverity.Warning, "Field required", "An e-mail is required in order to request a subscription");
            return;
        }

        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
        {
            App.Alert(AlertSeverity.Warning, "Wrong value", "The informed e-mail is not a valid email address");
            return;
        }

        await App.Service.UpdateOrAddSiteSubscription(siteId, email);
        await Close();
    }

    private async Task Close()
    {
        if (_previousIntent is not null)
        {
            await App.FlipToIntent(_previousIntent);
        }
        else
        {
            await Flip(Places.Subscriptions);
        }
    }
}
