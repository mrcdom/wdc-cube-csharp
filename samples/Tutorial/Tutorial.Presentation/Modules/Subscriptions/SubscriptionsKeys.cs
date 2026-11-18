using Tutorial.Presentation.Modules.Main;
using Tutorial.Presentation.Services;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Subscriptions;

public sealed class SubscriptionsKeys(CubeApplication app, FlipIntent? intent = null)
    : MainKeys(app, intent, Places.Subscriptions);

public sealed class SubscriptionsDetailKeys : MainKeys
{
    public new const string Prefix = "sd";

    private static class P
    {
        /// <summary>The site the dialog is about.</summary>
        public const string SiteId = $"{Prefix}-site";
    }

    private static class A
    {
        /// <summary>The site already in hand (the clicked row), so the dialog does not fetch it again.</summary>
        public const string Item = $"{Prefix}-item";
    }

    public SubscriptionsDetailKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Places.SubscriptionsDetail)
    {
    }

    /// <summary>A positive id, or <see langword="null"/> when the address carries none it can use.</summary>
    public long? SiteId
    {
        get => Intent.GetInt64(P.SiteId) is > 0 and var id ? id : null;
        set => Intent.SetParameter(P.SiteId, value);
    }

    public SiteItem? Item
    {
        get => Intent.GetAttribute<SiteItem>(A.Item);
        set => Intent.SetAttribute(A.Item, value);
    }
}
