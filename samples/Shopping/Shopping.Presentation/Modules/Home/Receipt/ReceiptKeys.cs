using Shopping.Presentation.Modules.Main;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Receipt;

public sealed class ReceiptKeys : HomeKeys
{
    public new const string Prefix = "rc";

    public static new Place Place { get; } = HomeKeys.Place.Child<MainPresenter>("receipt", app => new ReceiptPresenter(app));

    private static class P
    {
        /// <summary>The purchase shown.</summary>
        public const string PurchaseId = $"{Prefix}-id";
    }

    private static class A
    {
        /// <summary>The receipt comes right after buying: show the success banner.</summary>
        public const string PurchaseMade = $"{Prefix}-made";
    }

    public ReceiptKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }

    public long? PurchaseId
    {
        get => Intent.GetInt64(P.PurchaseId) is > 0 and var id ? id : null;
        set => Intent.SetParameter(P.PurchaseId, value);
    }

    public bool PurchaseMade
    {
        get => Intent.GetAttribute<bool?>(A.PurchaseMade) == true;
        set => Intent.SetAttribute(A.PurchaseMade, value ? true : null);
    }
}
