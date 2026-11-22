using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>
/// Who reaches what. The same tests run in process and
/// through HTTP: the rules are the protected repositories', not the transport's. The roles of the application follow
/// the business rules in every case; <c>suporte</c> (<see cref="Role.Super"/>) maintains the solution.
/// </summary>
public abstract class SecurityTestsBase(ShoppingDatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ShoppingRepositories Store => db.Store!.Repositories;

    /// <summary>The repositories of a session logged in as <paramref name="user"/> (whose password is the login).</summary>
    private async Task<(ShoppingRepositories Repositories, long UserId)> As(string user)
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var (repositories, authentication) = db.Connect();
        var subject = await authentication.Login(user, user, Ct);
        Assert.NotNull(subject);
        return (repositories, subject.Id);
    }

    private async Task<Product> AProduct() => (await Store.Products.Fetch(new ProductCriteria(), limit: 1, cancellationToken: Ct))[0];

    /// <summary>A purchase made by <paramref name="buyer"/>, with one item per amount.</summary>
    private async Task<Purchase> APurchase(ShoppingRepositories buyer, params int[] amounts)
    {
        var product = await AProduct();
        var purchase = new Purchase
        {
            BuyDate = DateTimeOffset.UtcNow,
            Items = [.. amounts.Select(a => new PurchaseItem { Product = new Product { Id = product.Id }, Amount = a })],
        };
        Assert.True(await buyer.Purchases.Insert(purchase, cancellationToken: Ct));
        return purchase;
    }

    private async Task<IReadOnlyList<PurchaseItem>> ItemsOf(Purchase purchase) =>
        await Store.PurchaseItems.Fetch(
            new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id).WithOrderBy(PurchaseItemOrderBy.OldestFirst), cancellationToken: Ct);

    private Task<bool> KeepOnlyTheFirstItem(ShoppingRepositories repositories, Purchase purchase, IReadOnlyList<PurchaseItem> items) =>
        repositories.Purchases.Update(
            new Purchase { Id = purchase.Id, Items = [new PurchaseItem { Id = items[0].Id, Amount = items[0].Amount }] },
            projection: new Purchase { Items = Mark.List(new PurchaseItem { Amount = Mark.Int32 }) }, cancellationToken: Ct);

    private Task<bool> AddAnItem(ShoppingRepositories repositories, Purchase purchase, IReadOnlyList<PurchaseItem> items, Product product) =>
        repositories.Purchases.Update(new Purchase
        {
            Id = purchase.Id,
            Items =
            [
                .. items.Select(i => new PurchaseItem { Id = i.Id, Amount = i.Amount, Product = new Product { Id = product.Id } }),
                new PurchaseItem { Amount = 4, Price = 0.01m, Product = new Product { Id = product.Id } }, // a price of its own
            ],
        }, projection: new Purchase { Items = Mark.List(new PurchaseItem { Amount = Mark.Int32, Product = new Product() }) },
            cancellationToken: Ct);

    // :: Who is acting

    [Fact]
    public async Task Nobody_IsServedNothing_AndAWrongPasswordLogsNobodyIn()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var (repositories, authentication) = db.Connect();

        await Assert.ThrowsAsync<AuthenticationException>(() => repositories.Products.Count(new ProductCriteria(), Ct));
        Assert.Null(await authentication.Login("fulano", "wrong", Ct));
        await Assert.ThrowsAsync<AuthenticationException>(() => repositories.Products.Count(new ProductCriteria(), Ct));
    }

    // :: The reach of each role

    [Fact]
    public async Task ACustomer_CannotWidenTheirReach_WithADisjunctiveRequest()
    {
        var (fulano, me) = await As("fulano");
        var criteria = new PurchaseCriteria();
        criteria.UserId.Or();
        criteria.UserId.IsNotNull(); // {"userId":{"or":true,"p":[{"o":"IS_NOT_NULL"}]}}
        criteria.WithProjection(new Purchase { Id = Mark.Int64, User = new User { Id = Mark.Int64 } });

        var purchases = await fulano.Purchases.Fetch(criteria, cancellationToken: Ct);

        Assert.All(purchases, p => Assert.Equal(me, p.User!.Id));
        Assert.True(await Store.Purchases.Count(new PurchaseCriteria(), Ct) > purchases.Count);
    }

    [Fact]
    public async Task ACustomer_ReadsOnlyThemselves_AndNeverAPassword()
    {
        var (fulano, me) = await As("fulano");

        var users = await fulano.Users.Fetch(
            new UserCriteria().WithProjection(new User { Id = Mark.Int64, Password = Mark.String }), cancellationToken: Ct);

        Assert.Equal([me], users.Select(u => u.Id));
        Assert.All(users, u => Assert.Null(u.Password));
    }

    [Fact]
    public async Task Super_ReachesEveryone_ButNeverAPassword()
    {
        var (suporte, _) = await As("suporte");

        var users = await suporte.Users.Fetch(
            new UserCriteria().WithProjection(new User { Id = Mark.Int64, Password = Mark.String }), cancellationToken: Ct);
        var purchases = await suporte.Purchases.Count(new PurchaseCriteria(), Ct);

        Assert.Equal(await Store.Users.Count(new UserCriteria(), Ct), users.Count);
        Assert.All(users, u => Assert.Null(u.Password));
        Assert.Equal(await Store.Purchases.Count(new PurchaseCriteria(), Ct), purchases);
    }

    [Fact]
    public async Task ACustomer_CannotWriteProducts()
    {
        var (fulano, _) = await As("fulano");

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            fulano.Products.Insert(new Product { Name = "Contrabando", Price = 1m }, cancellationToken: Ct));
    }

    // :: A purchase is made by whoever is acting, at the products' prices

    [Fact]
    public async Task APurchase_BelongsToWhoeverIsLoggedIn_AtTheProductsPrice()
    {
        var (fulano, me) = await As("fulano");
        var admin = (await Store.Users.Fetch(new UserCriteria().UserName.Eq("admin"), limit: 1, cancellationToken: Ct))[0];
        var product = await AProduct();
        var purchase = new Purchase
        {
            User = new User { Id = admin.Id }, // someone else's name on it
            BuyDate = DateTimeOffset.UtcNow,
            Items = [new PurchaseItem { Product = new Product { Id = product.Id }, Amount = 2, Price = 0.01m }],
        };

        Assert.True(await fulano.Purchases.Insert(purchase, cancellationToken: Ct));

        var stored = await Store.Purchases.FetchById(purchase.Id!.Value, new Purchase
        {
            User = new User { Id = Mark.Int64 },
            Items = Mark.List(new PurchaseItem { Price = Mark.Decimal }),
        }, Ct);
        Assert.Equal(me, stored!.User!.Id);
        Assert.Equal(product.Price, stored.Items!.Single().Price);
    }

    [Fact]
    public async Task APurchase_IsMadeWhenItIsCreated_SoItNeedsItsDate()
    {
        var (fulano, _) = await As("fulano");
        var product = await AProduct();

        await Assert.ThrowsAsync<InvalidRequestException>(() => fulano.Purchases.Insert(new Purchase
        {
            Items = [new PurchaseItem { Product = new Product { Id = product.Id }, Amount = 1 }],
        }, cancellationToken: Ct));
    }

    [Fact]
    public async Task Cascade_ACustomerCannotChangeAUser_ThroughAPurchase()
    {
        var (fulano, me) = await As("fulano");
        var purchase = new Purchase { User = new User { Id = me, Name = "Hacked" }, BuyDate = DateTimeOffset.UtcNow };

        await Assert.ThrowsAsync<AccessDeniedException>(() => fulano.Purchases.Insert(purchase,
            new Purchase { User = new User { Name = Mark.String }, BuyDate = Mark.Instant }, Ct));

        Assert.Equal("Fulano de Tal", (await Store.Users.FetchById(me, cancellationToken: Ct))!.Name);
    }

    // :: A purchase already made does not change, for every role of the application

    [Theory]
    [InlineData("fulano")]
    [InlineData("admin")]
    public async Task Made_ItsDateDoesNotChange(string user)
    {
        var (fulano, _) = await As("fulano");
        var purchase = await APurchase(fulano, 1);
        var (repositories, _) = user == "fulano" ? (fulano, 0L) : await As(user);

        await Assert.ThrowsAsync<AccessDeniedException>(() => repositories.Purchases.Update(
            new Purchase { Id = purchase.Id, BuyDate = DateTimeOffset.UnixEpoch },
            projection: new Purchase { BuyDate = Mark.Instant }, cancellationToken: Ct));

        var stored = await Store.Purchases.FetchById(purchase.Id!.Value, new Purchase { BuyDate = Mark.Instant }, Ct);
        Assert.NotEqual(DateTimeOffset.UnixEpoch, stored!.BuyDate);
    }

    [Theory]
    [InlineData("fulano")]
    [InlineData("admin")]
    public async Task Made_NoItemEntersIt(string user)
    {
        var (fulano, _) = await As("fulano");
        var purchase = await APurchase(fulano, 1);
        var items = await ItemsOf(purchase);
        var product = await AProduct();
        var (repositories, _) = user == "fulano" ? (fulano, 0L) : await As(user);

        await Assert.ThrowsAsync<AccessDeniedException>(() => AddAnItem(repositories, purchase, items, product)); // through it
        await Assert.ThrowsAsync<AccessDeniedException>(() => repositories.PurchaseItems.Insert( // by itself
            new PurchaseItem { Purchase = new Purchase { Id = purchase.Id }, Product = new Product { Id = product.Id }, Amount = 1 },
            cancellationToken: Ct));

        Assert.Single(await ItemsOf(purchase));
    }

    [Theory]
    [InlineData("fulano")]
    [InlineData("admin")]
    public async Task Made_ItsItemsDoNotChange(string user)
    {
        var (fulano, _) = await As("fulano");
        var purchase = await APurchase(fulano, 1);
        var item = (await ItemsOf(purchase))[0];
        var (repositories, _) = user == "fulano" ? (fulano, 0L) : await As(user);

        await Assert.ThrowsAsync<AccessDeniedException>(() => repositories.PurchaseItems.Update(
            new PurchaseItem { Id = item.Id, Amount = 9 }, projection: new PurchaseItem { Amount = Mark.Int32 }, cancellationToken: Ct));

        Assert.Equal(1, (await ItemsOf(purchase))[0].Amount);
    }

    [Theory]
    [InlineData("fulano")]
    [InlineData("admin")]
    public async Task Made_ItsItemsDoNotLeave_ThroughItOrByThemselves_AndNothingChanges(string user)
    {
        var (fulano, _) = await As("fulano");
        var purchase = await APurchase(fulano, 1, 2);
        var items = await ItemsOf(purchase);
        var (repositories, _) = user == "fulano" ? (fulano, 0L) : await As(user);

        // The full state without an item, and a new date in the same request.
        await Assert.ThrowsAsync<AccessDeniedException>(() => repositories.Purchases.Update(
            new Purchase { Id = purchase.Id, BuyDate = DateTimeOffset.UnixEpoch, Items = [] },
            projection: new Purchase { BuyDate = Mark.Instant, Items = Mark.List(new PurchaseItem { Amount = Mark.Int32 }) },
            cancellationToken: Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            repositories.PurchaseItems.Delete(new PurchaseItemCriteria().PurchaseItemId.Eq(items[1].Id), Ct));

        var stored = await Store.Purchases.FetchById(purchase.Id!.Value,
            new Purchase { BuyDate = Mark.Instant, Items = Mark.List(new PurchaseItem { Id = Mark.Int64 }) }, Ct);
        Assert.Equal(2, stored!.Items!.Count); // rolled back: the items are there...
        Assert.NotEqual(DateTimeOffset.UnixEpoch, stored.BuyDate); // ...and so is the date the same request tried to change
    }

    [Fact]
    public async Task Made_TheAdministratorOfTheApplicationDoesNotDeleteIt()
    {
        var (fulano, _) = await As("fulano");
        var purchase = await APurchase(fulano, 1);
        var (admin, _) = await As("admin");

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            admin.Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(purchase.Id), Ct));

        Assert.NotNull(await Store.Purchases.FetchById(purchase.Id!.Value, cancellationToken: Ct));
    }

    // :: The maintenance role amends it, by the other rules

    [Fact]
    public async Task Super_AmendsItsItems_AndANewOneTakesTheProductsPrice()
    {
        var (fulano, _) = await As("fulano");
        var purchase = await APurchase(fulano, 1, 2, 3);
        var items = await ItemsOf(purchase);
        var product = await AProduct();
        var (suporte, _) = await As("suporte");

        Assert.Equal(1, await suporte.PurchaseItems.Delete(new PurchaseItemCriteria().PurchaseItemId.Eq(items[2].Id), Ct));
        Assert.True(await KeepOnlyTheFirstItem(suporte, purchase, items));
        Assert.True(await AddAnItem(suporte, purchase, [items[0]], product));

        var stored = await ItemsOf(purchase);
        Assert.Equal([1, 4], stored.Select(i => i.Amount!.Value));
        Assert.Equal(product.Price, stored[1].Price); // never the price that was sent
    }

    [Fact]
    public async Task Super_DoesNotWriteThePriceOfAnItem()
    {
        var (fulano, _) = await As("fulano");
        var purchase = await APurchase(fulano, 1);
        var item = (await ItemsOf(purchase))[0];
        var (suporte, _) = await As("suporte");

        await Assert.ThrowsAsync<AccessDeniedException>(() => suporte.PurchaseItems.Update(
            new PurchaseItem { Id = item.Id, Price = 0.01m }, projection: new PurchaseItem { Price = Mark.Decimal },
            cancellationToken: Ct));

        Assert.Equal(item.Price, (await ItemsOf(purchase))[0].Price);
    }

    [Fact]
    public async Task Super_DeletesIt_WithItsItems()
    {
        var (fulano, _) = await As("fulano");
        var purchase = await APurchase(fulano, 1, 2);
        var (suporte, _) = await As("suporte");

        Assert.Equal(1, await suporte.Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(purchase.Id), Ct));

        Assert.Null(await Store.Purchases.FetchById(purchase.Id!.Value, cancellationToken: Ct));
        Assert.Empty(await ItemsOf(purchase));
    }
}
