using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// Who reaches what of the Shopping data, per role (<see cref="Role"/>): the data policy of each entity. The policies
/// refer to each other (a purchase reads its items through the items' policy), so they are built together.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Role.Super"/>, the maintenance role, reads and writes everything (the password only written).</item>
/// <item><see cref="Role.Admin"/> reads everything, maintains users and products, buys for anyone and corrects
/// purchases; a purchase made does not change for it.</item>
/// <item><see cref="Role.Manager"/> reads everything, maintains products and corrects purchases.</item>
/// <item><see cref="Role.Customer"/> reads the catalogue, themselves and their own purchases (without who corrected
/// them), and buys for themselves.</item>
/// </list>
/// What is not about access (a price from the product, a purchase made that does not change) is in
/// <see cref="ShoppingRules"/>.
/// </remarks>
public sealed class ShoppingPolicies
{
    /// <param name="store">The trusted inside, for a row check that needs what the row does not carry.</param>
    public ShoppingPolicies(ShoppingRepositories store)
    {
        ArgumentNullException.ThrowIfNull(store);
        Users = UserPolicy.Build();
        Products = ProductPolicy.Build();
        Purchases = PurchasePolicy.Build(this);
        Items = PurchaseItemPolicy.Build(this, store);
        Amendments = PurchaseAmendmentPolicy.Build(this, store);
        AmendmentLines = PurchaseAmendmentLinePolicy.Build(this, store);
    }

    public DataPolicy<User, UserCriteria> Users { get; }

    public DataPolicy<Product, ProductCriteria> Products { get; }

    public DataPolicy<Purchase, PurchaseCriteria> Purchases { get; }

    public DataPolicy<PurchaseItem, PurchaseItemCriteria> Items { get; }

    public DataPolicy<PurchaseAmendment, PurchaseAmendmentCriteria> Amendments { get; }

    public DataPolicy<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria> AmendmentLines { get; }

    internal static readonly string[] Super = [nameof(Role.Super)];

    internal static readonly string[] Admin = [nameof(Role.Admin)];

    internal static readonly string[] Manager = [nameof(Role.Manager)];

    internal static readonly string[] Customer = [nameof(Role.Customer)];

    internal static long IdOf(Actor actor) => long.Parse(actor.Id, System.Globalization.CultureInfo.InvariantCulture);
}
