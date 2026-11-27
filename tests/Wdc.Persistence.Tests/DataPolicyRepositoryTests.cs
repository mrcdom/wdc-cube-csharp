using System.Globalization;
using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>
/// The repository protected by data policies, on PostgreSQL: the reads are restricted and shaped, and every child a
/// write touches in cascade answers to its own entity's grants, on insert, update and delete.
/// </summary>
public sealed class DataPolicyRepositoryTests(PostgreSqlFixture fixture) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Shopping Local => fixture.Shopping!;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(fixture.Shopping is null, fixture.SkipReason);
        await fixture.Reset();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // :: The policies: buyers reach their own purchases and items; keepers read everything and delete purchases.

    private static long IdOf(Actor actor) => long.Parse(actor.Id, CultureInfo.InvariantCulture);

    private sealed class OwnPurchases : IRowScope<Purchase, PurchaseCriteria>
    {
        public static readonly OwnPurchases Scope = new();

        public void Restrict(PurchaseCriteria group, Actor actor) => group.UserId.Eq(IdOf(actor));

        public ValueTask<bool> Covers(Purchase row, Actor actor, CancellationToken cancellationToken) =>
            ValueTask.FromResult(row.User?.Id == IdOf(actor));

        public Purchase? Projection() => new() { User = new User { Id = Mark.Int64 } };
    }

    /// <summary>The items of one's purchases: looked up when the item carries only its purchase's key.</summary>
    private sealed class OwnItems(Shopping store) : IRowScope<PurchaseItem, PurchaseItemCriteria>
    {
        // The test domain's item criteria has no buyer field: the buyer's purchases are listed.
        public void Restrict(PurchaseItemCriteria group, Actor actor) =>
            group.PurchaseId.In(OwnPurchaseIds(actor).GetAwaiter().GetResult());

        public async ValueTask<bool> Covers(PurchaseItem row, Actor actor, CancellationToken cancellationToken)
        {
            var owner = row.Purchase?.User?.Id;
            if (owner is null && row.Purchase?.Id is { } id)
            {
                owner = (await store.Purchases.FetchById(id, new Purchase { User = new User { Id = Mark.Int64 } }, cancellationToken))?.User?.Id;
            }

            return owner == IdOf(actor);
        }

        public PurchaseItem? Projection() => new() { Purchase = new Purchase { User = new User { Id = Mark.Int64 } } };

        private async Task<IEnumerable<long?>> OwnPurchaseIds(Actor actor) =>
            (await store.Purchases.Fetch(new PurchaseCriteria().UserId.Eq(IdOf(actor)).WithProjection(new Purchase { Id = Mark.Int64 })))
            .Select(p => p.Id);
    }

    private sealed record Policies(
        DataPolicy<User, UserCriteria> Users,
        DataPolicy<Product, ProductCriteria> Products,
        DataPolicy<Purchase, PurchaseCriteria> Purchases,
        DataPolicy<PurchaseItem, PurchaseItemCriteria> Items);

    /// <param name="buyerItems">What a buyer may do with the items of their purchases, besides reading and creating.</param>
    private Policies Build(Action<DataGrantBuilder<PurchaseItem, PurchaseItemCriteria>>? buyerItems = null)
    {
        Policies? all = null;
        var users = DataPolicy.For(User.Model, () => new UserCriteria())
            .WriteOnly("password")
            .Grant(["keeper", "buyer"], g => g.Read("userName", "name", "role"))
            .Build();
        var products = DataPolicy.For(Product.Model, () => new ProductCriteria())
            .Collection("items", inverse: "product", () => all!.Items)
            .Criterion("productId", "id")
            .Criterion("search", "name", "description")
            .Orderings(c => c.OrderBy == ProductOrderBy.CheapestFirst ? ["price"] : c.OrderBy == ProductOrderBy.NameAToZ ? ["name"] : [])
            .Grant(["keeper", "buyer"], g => g.Read("name", "price", "description", "image"))
            .Build();
        var purchases = DataPolicy.For(Purchase.Model, () => new PurchaseCriteria())
            .Reference("user", () => all!.Users)
            .Collection("items", inverse: "purchase", () => all!.Items)
            .Criterion("purchaseId", "id")
            .Criterion("userId", "user")
            .Criterion("productId", "items.product")
            .Grant(["keeper"], g => g.Read("buyDate", "user", "items").Delete())
            .Grant(["buyer"], g => g.Rows(OwnPurchases.Scope).Read("buyDate", "user", "items")
                .Create("buyDate", "user", "items").Update("buyDate", "items"))
            .Build();
        var items = DataPolicy.For(PurchaseItem.Model, () => new PurchaseItemCriteria())
            .Reference("purchase", () => all!.Purchases)
            .Reference("product", () => all!.Products)
            .Criterion("purchaseItemId", "id")
            .Criterion("purchaseId", "purchase")
            .Grant(["keeper"], g => g.Read("amount", "price", "purchase", "product").Delete())
            .Grant(["buyer"], g =>
            {
                g.Rows(new OwnItems(Local)).Read("amount", "price", "purchase", "product").Create("amount", "price", "purchase", "product");
                buyerItems?.Invoke(g);
            })
            .Build();
        all = new Policies(users, products, purchases, items);
        return all;
    }

    private sealed record Side(
        ActorSource Actors,
        ProtectedRepository<Purchase, PurchaseCriteria, long> Purchases,
        ProtectedRepository<PurchaseItem, PurchaseItemCriteria, long> Items,
        ProtectedRepository<User, UserCriteria, long> Users,
        ProtectedRepository<Product, ProductCriteria, long> Products)
    {
        public Side As(User user, params string[] roles)
        {
            Actors.Current = new Actor(user.Id!.Value.ToString(CultureInfo.InvariantCulture), roles);
            return this;
        }
    }

    private Side Protect(Policies? policies = null)
    {
        policies ??= Build();
        var actors = new ActorSource();
        var protection = new Protection(actors, Local.Database);
        return new Side(actors,
            new(Local.Purchases, policies.Purchases, (c, id) => c.PurchaseId.Restrict(id), protection),
            new(Local.Items, policies.Items, (c, id) => c.PurchaseItemId.Restrict(id), protection),
            new(Local.Users, policies.Users, (_, _) => throw new NotSupportedException("no key criterion"), protection),
            new(Local.Products, policies.Products, (c, id) => c.ProductId.Restrict(id), protection));
    }

    private async Task<User> NewUser(string name)
    {
        var user = new User { UserName = name, Name = name, Password = "secret-" + name };
        await Local.Users.Insert(user, cancellationToken: Ct);
        return user;
    }

    private async Task<Product> NewProduct(string name = "Ball", string? description = null)
    {
        var product = new Product { Name = name, Price = 10m, Description = description };
        await Local.Products.Insert(product, cancellationToken: Ct);
        return product;
    }

    private async Task<Purchase> Buys(Side side, User buyer, Product product, params int[] amounts)
    {
        var purchase = new Purchase
        {
            User = new User { Id = buyer.Id },
            BuyDate = DateTimeOffset.UtcNow,
            Items = [.. amounts.Select(a => new PurchaseItem { Product = new Product { Id = product.Id }, Amount = a, Price = 10m })],
        };
        Assert.True(await side.As(buyer, "buyer").Purchases.Insert(purchase, cancellationToken: Ct));
        return purchase;
    }

    private async Task<int[]> AmountsOf(Purchase purchase) =>
        [.. (await Local.Items.Fetch(new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id).WithOrderBy(PurchaseItemOrderBy.OldestFirst)
            .WithProjection(new PurchaseItem { Amount = Mark.Int32 }), cancellationToken: Ct)).Select(i => i.Amount!.Value)];

    private static Purchase ItemsOnly(Purchase purchase, params PurchaseItem[] items) => new() { Id = purchase.Id, Items = [.. items] };

    private static readonly Purchase s_itemsWritten = new() { Items = Mark.List(new PurchaseItem { Amount = Mark.Int32, Product = new Product() }) };

    // :: Reads

    [Fact]
    public async Task ABuyer_ReadsTheirOwnPurchases_FiltersThroughTheItems_AndNeverAPassword()
    {
        var ana = await NewUser("ana");
        var bia = await NewUser("bia");
        var ball = await NewProduct();
        var side = Protect();
        var mine = await Buys(side, ana, ball, 1);
        var hers = await Buys(side, bia, ball, 2);

        var found = await side.As(ana, "buyer").Purchases.Fetch(new PurchaseCriteria().ProductId.Eq(ball.Id), cancellationToken: Ct);

        Assert.Equal([mine.Id], found.Select(p => p.Id));
        Assert.Equal(1, await side.Purchases.Count(new PurchaseCriteria(), Ct));
        Assert.Null(await side.Purchases.FetchById(hers.Id!.Value, cancellationToken: Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => side.As(ana, "buyer").Users.Fetch(
            new UserCriteria().WithProjection(new User { Password = Mark.String }), cancellationToken: Ct));
    }

    [Fact]
    public async Task ACriterionOverSeveralFields_TakesReadingThemAll()
    {
        var ana = await NewUser("ana");
        await NewProduct("Kit", "with a needle");
        var reader = Build();
        var nameOnly = new Policies(reader.Users, DataPolicy.For(Product.Model, () => new ProductCriteria())
            .Collection("items", inverse: "product", () => reader.Items)
            .Criterion("productId", "id")
            .Criterion("search", "name", "description")
            .Grant(["buyer"], g => g.Read("name", "price"))
            .Build(), reader.Purchases, reader.Items);

        Assert.Single(await Protect(reader).As(ana, "buyer").Products.Fetch(new ProductCriteria().Search.Containing("needle"), cancellationToken: Ct));
        var e = await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Protect(nameOnly).As(ana, "buyer").Products.Fetch(new ProductCriteria().Search.Containing("needle"), cancellationToken: Ct));
        Assert.Contains("description", e.Message, StringComparison.Ordinal);
    }

    // :: Children in cascade

    [Fact]
    public async Task Insert_EachChildIsChecked_AsARowOfTheNewOne()
    {
        var ana = await NewUser("ana");
        var bia = await NewUser("bia");
        var ball = await NewProduct();
        var side = Protect();
        var hers = await Buys(side, bia, ball, 1);

        var mine = await Buys(side, ana, ball, 1, 2);

        Assert.Equal(new[] { 1, 2 }, await AmountsOf(mine));
        // An item by itself into someone else's purchase is out of reach.
        await Assert.ThrowsAsync<AccessDeniedException>(() => side.As(ana, "buyer").Items.Insert(
            new PurchaseItem { Purchase = new Purchase { Id = hers.Id }, Product = new Product { Id = ball.Id }, Amount = 5, Price = 1m },
            cancellationToken: Ct));
        Assert.Equal(new[] { 1 }, await AmountsOf(hers));
    }

    [Fact]
    public async Task Update_ANewChildNeedsTheCreateGrant_AChangedOneTheUpdateGrant_AnOrphanTheDeleteGrant()
    {
        var ana = await NewUser("ana");
        var ball = await NewProduct();
        var side = Protect();
        var purchase = await Buys(side, ana, ball, 1, 2);
        var items = await Local.Items.Fetch(new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id).WithOrderBy(PurchaseItemOrderBy.OldestFirst)
            .WithProjection(new PurchaseItem { Amount = Mark.Int32, Product = new Product { Id = Mark.Int64 } }), cancellationToken: Ct);
        PurchaseItem Kept(int index, int amount) => new() { Id = items[index].Id, Amount = amount, Product = new Product { Id = ball.Id } };
        var buyer = side.As(ana, "buyer");

        // A new item: the buyer creates items of their purchases.
        Assert.True(await buyer.Purchases.Update(ItemsOnly(purchase, Kept(0, 1), Kept(1, 2),
            new PurchaseItem { Amount = 3, Price = 10m, Product = new Product { Id = ball.Id } }), projection: s_itemsWritten, cancellationToken: Ct));
        Assert.Equal(new[] { 1, 2, 3 }, await AmountsOf(purchase));
        var third = (await Local.Items.Fetch(new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id).Amount.Eq(3), cancellationToken: Ct))[0];

        // A changed amount: no update grant on the items; refused, and nothing changes.
        await Assert.ThrowsAsync<AccessDeniedException>(() => buyer.Purchases.Update(ItemsOnly(purchase, Kept(0, 9), Kept(1, 2),
            new PurchaseItem { Id = third.Id, Amount = 3, Product = new Product { Id = ball.Id } }), projection: s_itemsWritten, cancellationToken: Ct));
        // An orphan: no delete grant on the items; refused, and nothing changes.
        await Assert.ThrowsAsync<AccessDeniedException>(() => buyer.Purchases.Update(ItemsOnly(purchase, Kept(0, 1), Kept(1, 2)),
            projection: s_itemsWritten, cancellationToken: Ct));
        Assert.Equal(new[] { 1, 2, 3 }, await AmountsOf(purchase));

        // With those grants, the same updates go through.
        var granted = Protect(Build(g => g.Update("amount").Delete())).As(ana, "buyer");
        Assert.True(await granted.Purchases.Update(ItemsOnly(purchase, Kept(0, 9), Kept(1, 2)), projection: s_itemsWritten, cancellationToken: Ct));
        Assert.Equal(new[] { 9, 2 }, await AmountsOf(purchase));
    }

    [Fact]
    public async Task Delete_TheChildrenDeletedWithTheirParent_NeedTheirOwnDeleteGrant()
    {
        var ana = await NewUser("ana");
        var keeper = await NewUser("keeper");
        var ball = await NewProduct();
        var side = Protect();
        var purchase = await Buys(side, ana, ball, 1);
        var withoutItemDelete = Build();
        withoutItemDelete = withoutItemDelete with
        {
            Items = DataPolicy.For(PurchaseItem.Model, () => new PurchaseItemCriteria())
                .Reference("purchase", () => withoutItemDelete.Purchases)
                .Reference("product", () => withoutItemDelete.Products)
                .Criterion("purchaseItemId", "id")
                .Criterion("purchaseId", "purchase")
                .Grant(["keeper"], g => g.Read("amount", "price", "purchase", "product"))
                .Build(),
        };

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Protect(withoutItemDelete).As(keeper, "keeper").Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(purchase.Id), Ct));
        Assert.Equal(new[] { 1 }, await AmountsOf(purchase));

        Assert.Equal(1, await side.As(keeper, "keeper").Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(purchase.Id), Ct));
        Assert.Empty(await AmountsOf(purchase));
    }

    [Fact]
    public async Task Update_ARowOutOfReach_IsNotFound()
    {
        var ana = await NewUser("ana");
        var bia = await NewUser("bia");
        var ball = await NewProduct();
        var side = Protect();
        var hers = await Buys(side, bia, ball, 1);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => side.As(ana, "buyer").Purchases.Update(
            new Purchase { Id = hers.Id, BuyDate = DateTimeOffset.UnixEpoch }, projection: new Purchase { BuyDate = Mark.Instant },
            cancellationToken: Ct));
    }
}
