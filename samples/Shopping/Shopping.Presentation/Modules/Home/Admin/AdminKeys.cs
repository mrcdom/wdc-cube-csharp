using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Admin;

/// <summary>Names of the administration: the purchases of every customer.</summary>
public class AdminKeys : HomeKeys
{
    public new const string Prefix = "ad";

    /// <summary>The purchases of every customer: only for whoever may correct them.</summary>
    public static new Place Place { get; } = HomeKeys.Place.Child<RootPresenter>("admin", app => new AdminPresenter(app));

    private static class P
    {
        /// <summary>The page of the list (1-based; the first stays out of the address).</summary>
        public const string Page = $"{Prefix}-page";

        /// <summary>Part of the customer's name.</summary>
        public const string Customer = $"{Prefix}-customer";
    }

    private static class A
    {
        /// <summary>The administration presenter, for the places below it (they go back to its list).</summary>
        public const string Admin = $"{Prefix}-admin";
    }

    public AdminKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected AdminKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }

    public int ListPage
    {
        get => Intent.GetInt32(P.Page) is > 1 and var page ? page : 1;
        set => Intent.SetParameter(P.Page, value > 1 ? value : null);
    }

    /// <summary>Blank reads as none.</summary>
    public string? Customer
    {
        get => Intent.GetString(P.Customer) is { } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
        set => Intent.SetParameter(P.Customer, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public AdminPresenter? Admin
    {
        get => Intent.GetAttribute<AdminPresenter>(A.Admin);
        set => Intent.SetAttribute(A.Admin, value);
    }
}
