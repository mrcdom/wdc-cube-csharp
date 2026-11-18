using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>Who reaches what through the Shopping server (decision D-19 and bug 13 of the Kotlin report).</summary>
public sealed class ApiSecurityTests(ShoppingHttpFixture db) : IClassFixture<ShoppingHttpFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Client.ShoppingClient Client, long UserId)> As(string user)
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var client = db.NewClient();
        var subject = await client.Authentication.Login(user, user, Ct);
        Assert.NotNull(subject);
        return (client, subject.Id);
    }

    [Fact]
    public async Task WithoutASession_NothingIsServed()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var client = db.NewClient();

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Repositories.Products.Count(new ProductCriteria(), Ct));
        Assert.Null(await client.Authentication.Login("fulano", "wrong", Ct));
    }

    [Fact]
    public async Task ACustomer_CannotWidenTheirReach_WithADisjunctiveRequest()
    {
        var (client, me) = await As("fulano");
        var criteria = new PurchaseCriteria();
        criteria.UserId.Or();
        criteria.UserId.IsNotNull(); // {"userId":{"or":true,"p":[{"o":"IS_NOT_NULL"}]}}
        criteria.WithProjection(new Purchase { Id = Mark.Int64, User = new User { Id = Mark.Int64 } });

        var purchases = await client.Repositories.Purchases.Fetch(criteria, cancellationToken: Ct);

        Assert.All(purchases, p => Assert.Equal(me, p.User!.Id));
        Assert.True(await db.Store!.Repositories.Purchases.Count(new PurchaseCriteria(), Ct) > purchases.Count);
    }

    [Fact]
    public async Task ACustomer_ReadsOnlyThemselves_AndNeverAPassword()
    {
        var (client, me) = await As("fulano");

        var users = await client.Repositories.Users.Fetch(
            new UserCriteria().WithProjection(new User { Id = Mark.Int64, Password = Mark.String }), cancellationToken: Ct);

        Assert.Equal([me], users.Select(u => u.Id));
        Assert.All(users, u => Assert.Null(u.Password));
    }

    [Fact]
    public async Task ACustomer_CannotWriteProducts()
    {
        var (client, _) = await As("fulano");

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            client.Repositories.Products.Insert(new Product { Name = "Contrabando", Price = 1m }, cancellationToken: Ct));
    }

    [Fact]
    public async Task APurchase_BelongsToWhoeverIsLoggedIn_AtTheProductsPrice()
    {
        var (client, me) = await As("fulano");
        var admin = (await db.Store!.Repositories.Users.Fetch(new UserCriteria().UserName.Eq("admin"), limit: 1, cancellationToken: Ct))[0];
        var product = (await db.Store.Repositories.Products.Fetch(new ProductCriteria(), limit: 1, cancellationToken: Ct))[0];
        var purchase = new Purchase
        {
            User = new User { Id = admin.Id }, // someone else's name on it
            BuyDate = DateTimeOffset.UtcNow,
            Items = [new PurchaseItem { Product = new Product { Id = product.Id }, Amount = 2, Price = 0.01m }],
        };

        Assert.True(await client.Repositories.Purchases.Insert(purchase, cancellationToken: Ct));

        var stored = await db.Store.Repositories.Purchases.FetchById(purchase.Id!.Value, new Purchase
        {
            User = new User { Id = Mark.Int64 },
            Items = Mark.List(new PurchaseItem { Price = Mark.Decimal }),
        }, Ct);
        Assert.Equal(me, stored!.User!.Id);
        Assert.Equal(product.Price, stored.Items!.Single().Price);
    }

    // :: Writes in cascade answer to the child's rules (decision D-23)

    [Fact]
    public async Task Cascade_ACustomerCannotChangeAUser_ThroughAPurchase()
    {
        var (client, me) = await As("fulano");
        var purchase = new Purchase { User = new User { Id = me, Name = "Hacked" }, BuyDate = DateTimeOffset.UtcNow };

        await Assert.ThrowsAsync<AccessDeniedException>(() => client.Repositories.Purchases.Insert(purchase,
            new Purchase { User = new User { Name = Mark.String }, BuyDate = Mark.Instant }, Ct));

        Assert.Equal("Fulano de Tal", (await db.Store!.Repositories.Users.FetchById(me, cancellationToken: Ct))!.Name);
    }

    [Fact]
    public async Task Cascade_ItemsAddedByAnUpdate_GetThePriceOfTheProduct()
    {
        var (client, _) = await As("fulano");
        var (product, purchase) = await AnOwnPurchase(client, DateTimeOffset.UtcNow);
        var existing = (await db.Store!.Repositories.PurchaseItems.Fetch(new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id),
            cancellationToken: Ct)).Single();

        await client.Repositories.Purchases.Update(new Purchase
        {
            Id = purchase.Id,
            Items =
            [
                new PurchaseItem { Id = existing.Id, Amount = 2, Product = new Product { Id = product.Id } },
                new PurchaseItem { Amount = 4, Price = 0.01m, Product = new Product { Id = product.Id } }, // a price of its own
            ],
        }, projection: new Purchase { Items = Mark.List(new PurchaseItem { Amount = Mark.Int32, Product = new Product() }) },
            cancellationToken: Ct);

        var items = await db.Store.Repositories.PurchaseItems.Fetch(
            new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id).WithOrderBy(PurchaseItemOrderBy.OldestFirst), cancellationToken: Ct);
        Assert.Equal([2, 4], items.Select(i => i.Amount!.Value));
        Assert.All(items, i => Assert.Equal(product.Price, i.Price)); // never the price the client sent
    }

    // :: A purchase already made keeps its items; only the administrator amends it

    private async Task<(Product Product, Purchase Purchase)> AnOwnPurchase(Client.ShoppingClient client, DateTimeOffset? buyDate,
        int items = 1)
    {
        var product = (await db.Store!.Repositories.Products.Fetch(new ProductCriteria(), limit: 1, cancellationToken: Ct))[0];
        var purchase = new Purchase
        {
            BuyDate = buyDate,
            Items = [.. Enumerable.Range(1, items).Select(n => new PurchaseItem { Product = new Product { Id = product.Id }, Amount = n })],
        };
        Assert.True(await client.Repositories.Purchases.Insert(purchase, cancellationToken: Ct));
        return (product, purchase);
    }

    private async Task<IReadOnlyList<PurchaseItem>> ItemsOf(Purchase purchase) =>
        await db.Store!.Repositories.PurchaseItems.Fetch(
            new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id).WithOrderBy(PurchaseItemOrderBy.OldestFirst), cancellationToken: Ct);

    private Task<bool> KeepOnlyTheFirstItem(Client.ShoppingClient client, Purchase purchase, IReadOnlyList<PurchaseItem> items) =>
        client.Repositories.Purchases.Update(
            new Purchase { Id = purchase.Id, Items = [new PurchaseItem { Id = items[0].Id, Amount = items[0].Amount }] },
            projection: new Purchase { Items = Mark.List(new PurchaseItem { Amount = Mark.Int32 }) }, cancellationToken: Ct);

    [Fact]
    public async Task Bought_ACustomerCannotRemoveItems_ThroughThePurchase_AndNothingChanges()
    {
        var (client, _) = await As("fulano");
        var (_, purchase) = await AnOwnPurchase(client, DateTimeOffset.UtcNow);

        // The full state without the item, and a new date in the same request.
        await Assert.ThrowsAsync<AccessDeniedException>(() => client.Repositories.Purchases.Update(
            new Purchase { Id = purchase.Id, BuyDate = DateTimeOffset.UnixEpoch, Items = [] },
            projection: new Purchase { BuyDate = Mark.Instant, Items = Mark.List(new PurchaseItem { Amount = Mark.Int32 }) },
            cancellationToken: Ct));

        var stored = await db.Store!.Repositories.Purchases.FetchById(purchase.Id!.Value,
            new Purchase { BuyDate = Mark.Instant, Items = Mark.List(new PurchaseItem { Id = Mark.Int64 }) }, Ct);
        Assert.Single(stored!.Items!); // rolled back: the item is there...
        Assert.NotEqual(DateTimeOffset.UnixEpoch, stored.BuyDate); // ...and so is the date the same request tried to change
    }

    [Fact]
    public async Task Bought_ACustomerCannotRemoveAnItem_ByItself()
    {
        var (client, _) = await As("fulano");
        var (_, purchase) = await AnOwnPurchase(client, DateTimeOffset.UtcNow, items: 2);
        var items = await ItemsOf(purchase);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            client.Repositories.PurchaseItems.Delete(new PurchaseItemCriteria().PurchaseItemId.Eq(items[1].Id), Ct));

        Assert.Equal(2, (await ItemsOf(purchase)).Count);
    }

    [Fact]
    public async Task APurchase_IsMadeWhenItIsCreated_SoItNeedsItsDate()
    {
        var (client, _) = await As("fulano");

        await Assert.ThrowsAsync<InvalidRequestException>(() => AnOwnPurchase(client, buyDate: null));
    }

    [Fact]
    public async Task Bought_TheAdministratorRemovesItems_ThroughThePurchase_OrOneByOne()
    {
        var (customer, _) = await As("fulano");
        var (_, purchase) = await AnOwnPurchase(customer, DateTimeOffset.UtcNow, items: 3);
        var items = await ItemsOf(purchase);
        var (admin, _) = await As("admin");

        Assert.Equal(1, await admin.Repositories.PurchaseItems.Delete(new PurchaseItemCriteria().PurchaseItemId.Eq(items[2].Id), Ct));
        Assert.True(await KeepOnlyTheFirstItem(admin, purchase, items));

        Assert.Equal([items[0].Id], (await ItemsOf(purchase)).Select(i => i.Id));
    }
}
