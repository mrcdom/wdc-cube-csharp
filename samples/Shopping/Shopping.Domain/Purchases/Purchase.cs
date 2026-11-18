using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public sealed class Purchase : IKeyedEntity
{
    public static readonly EntityModel<Purchase> Model = EntityModel.For(() => new Purchase())
        .Key(ValueKinds.Int64, "id", p => p.Id, (p, v) => p.Id = v)
        .Field(ValueKinds.Instant, "buyDate", p => p.BuyDate, (p, v) => p.BuyDate = v)
        .Reference("user", p => p.User, (p, v) => p.User = v, () => User.Model)
        .Collection("items", p => p.Items, (p, v) => p.Items = v, () => PurchaseItem.Model, () => new PurchaseItemCriteria())
        .Build();

    public long? Id { get; set; }

    public DateTimeOffset? BuyDate { get; set; }

    public User? User { get; set; }

    public List<PurchaseItem>? Items { get; set; }

    object? IKeyedEntity.Key => Id;
}
