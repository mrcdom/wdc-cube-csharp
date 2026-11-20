using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Admin.Amend;

public sealed class AmendKeys : AdminKeys
{
    public new const string Prefix = "am";

    private static class P
    {
        /// <summary>The purchase being corrected.</summary>
        public const string PurchaseId = $"{Prefix}-id";
    }

    public AmendKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Places.Amend)
    {
    }

    public long? PurchaseId
    {
        get => Intent.GetInt64(P.PurchaseId) is > 0 and var id ? id : null;
        set => Intent.SetParameter(P.PurchaseId, value);
    }
}
