using Shopping.Presentation.Modules.Root;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.ProductDetail;

public sealed class ProductKeys : HomeKeys
{
    public new const string Prefix = "pr";

    public static new Place Place { get; } = HomeKeys.Place.Child<RootPresenter>("product", app => new ProductPresenter(app));

    private static class P
    {
        /// <summary>The product shown.</summary>
        public const string ProductId = $"{Prefix}-id";
    }

    public ProductKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }

    /// <summary>A positive id, or <see langword="null"/> when the address carries none it can use.</summary>
    public long? ProductId
    {
        get => Intent.GetInt64(P.ProductId) is > 0 and var id ? id : null;
        set => Intent.SetParameter(P.ProductId, value);
    }
}
