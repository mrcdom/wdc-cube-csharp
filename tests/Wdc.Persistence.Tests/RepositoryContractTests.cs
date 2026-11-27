using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>
/// The repository contract, written once and run against every database (each subclass brings its fixture).
/// </summary>
public abstract class RepositoryContractTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private IShoppingSide Db => fixture.Side!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(fixture.Shopping is null, fixture.SkipReason);
        await fixture.Reset();
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private async Task<Product> NewProduct(string name, decimal? price, string? description = null)
    {
        var product = new Product { Name = name, Price = price, Description = description };
        Assert.True(await Db.Products.Insert(product, cancellationToken: Ct));
        return product;
    }

    private async Task<User> NewUser(string userName, string name)
    {
        var user = new User { UserName = userName, Name = name, Password = "secret" };
        Assert.True(await Db.Users.Insert(user, cancellationToken: Ct));
        return user;
    }

    private async Task<Purchase> NewPurchase(User user, params (Product Product, int Amount)[] lines)
    {
        var purchase = new Purchase { User = user, BuyDate = DateTimeOffset.UtcNow };
        await Db.Tx.Required(async _ =>
        {
            await Db.Purchases.Insert(purchase, cancellationToken: Ct);
            purchase.Items = [];
            foreach (var (product, amount) in lines)
            {
                var item = new PurchaseItem { Purchase = purchase, Product = product, Amount = amount, Price = product.Price };
                await Db.Items.Insert(item, cancellationToken: Ct);
                purchase.Items.Add(item);
            }
        }, Ct);
        return purchase;
    }

    // :: Insert and fetch

    [Fact]
    public async Task Insert_CompletesTheBean_WithTheGeneratedKeyAndTheDefaults()
    {
        var user = await NewUser("ana", "Ana");
        var purchase = new Purchase { User = user }; // buy_date and id come from the database

        Assert.True(await Db.Purchases.Insert(purchase, cancellationToken: Ct));

        Assert.True(purchase.Id > 0);
        Assert.NotNull(purchase.BuyDate);
        Assert.Equal(Role.Customer, user.Role); // column default read back
    }

    [Fact]
    public async Task Insert_WithProjection_WritesOnlyTheMarkedFields()
    {
        var product = new Product { Name = "Ball", Price = 10m, Description = "not written" };

        await Db.Products.Insert(product, new Product { Name = Mark.String, Price = Mark.Decimal }, Ct);

        var stored = await Db.Products.FetchById(product.Id!.Value, cancellationToken: Ct);
        Assert.Null(stored!.Description);
        Assert.Null(product.Description); // the bean comes back as stored
    }

    [Fact]
    public async Task Fetch_DefaultProjection_OwnFieldsAndReferenceKeys_NoBlobsSecretsOrCollections()
    {
        var user = await NewUser("bia", "Bia");
        var product = await NewProduct("Ball", 10m);
        await Db.Products.Update(new Product { Id = product.Id, Image = [1, 2, 3] },
            projection: new Product { Image = Mark.Bytes }, cancellationToken: Ct);
        await NewPurchase(user, (product, 1));

        var storedUser = (await Db.Users.Fetch(new UserCriteria().UserName.Eq("bia"), cancellationToken: Ct)).Single();
        var storedProduct = await Db.Products.FetchById(product.Id!.Value, cancellationToken: Ct);
        var purchase = (await Db.Purchases.Fetch(new PurchaseCriteria(), cancellationToken: Ct)).Single();

        Assert.Equal("Bia", storedUser.Name);
        Assert.Null(storedUser.Password);
        Assert.Null(storedProduct!.Image);
        Assert.Equal(user.Id, purchase.User!.Id);
        Assert.Null(purchase.User.Name); // key only: read from the purchase row itself
        Assert.Null(purchase.Items);
    }

    [Fact]
    public async Task Fetch_NestedProjection_ReferencesAndAFilteredOrderedSlicedCollection_InOneStatement()
    {
        var user = await NewUser("caio", "Caio");
        var cheap = await NewProduct("Tape", 2m);
        var mid = await NewProduct("Ball", 10m);
        var dear = await NewProduct("Racket", 99.9m);
        var purchase = await NewPurchase(user, (cheap, 5), (mid, 2), (dear, 1));

        var projection = new Purchase
        {
            Id = Mark.Int64,
            BuyDate = Mark.Instant,
            User = new User { Id = Mark.Int64, Name = Mark.String },
            Items = Mark.List(
                new PurchaseItem { Id = Mark.Int64, Price = Mark.Decimal, Product = new Product { Name = Mark.String } },
                new PurchaseItemCriteria().Amount.Le(2).WithOrderBy(PurchaseItemOrderBy.MostExpensiveFirst),
                limit: 1),
        };
        var stored = (await Db.Purchases.Fetch(new PurchaseCriteria().WithProjection(projection), cancellationToken: Ct)).Single();

        Assert.Equal(purchase.Id, stored.Id);
        Assert.Equal("Caio", stored.User!.Name);
        var item = Assert.Single(stored.Items!); // amount <= 2, most expensive first, limit 1
        Assert.Equal(99.9m, item.Price);
        Assert.Equal("Racket", item.Product!.Name);
    }

    [Fact]
    public async Task Fetch_TheKeyComesAtEveryLevel_EvenWhenTheProjectionDoesNotMarkIt()
    {
        var user = await NewUser("dora", "Dora");
        var product = await NewProduct("Tape", 2m);
        var purchase = await NewPurchase(user, (product, 3));
        var projection = new Purchase
        {
            BuyDate = Mark.Instant,
            User = new User { Name = Mark.String },
            Items = Mark.List(new PurchaseItem { Amount = Mark.Int32, Product = new Product { Name = Mark.String } }),
        };

        var fetched = (await Db.Purchases.Fetch(new PurchaseCriteria().WithProjection(projection), cancellationToken: Ct)).Single();
        var byId = (await Db.Purchases.FetchById(purchase.Id!.Value, projection, Ct))!;

        foreach (var stored in new[] { fetched, byId })
        {
            Assert.Equal(purchase.Id, stored.Id);
            Assert.Equal(user.Id, stored.User!.Id);
            var item = Assert.Single(stored.Items!);
            Assert.Equal(purchase.Items![0].Id, item.Id);
            Assert.Equal(product.Id, item.Product!.Id);
        }
    }

    [Fact]
    public async Task Relation_TheChildRepositorysConditionsFilterTheCollection_WithAndWithoutASlice()
    {
        var user = await NewUser("eva", "Eva");
        var cheap = await NewProduct("Tape", 2m);
        var mid = await NewProduct("Ball", 10m);
        var dear = await NewProduct("Racket", 99.9m);
        await NewPurchase(user, (cheap, 5), (mid, 2), (dear, 1));

        async Task<string[]> Names(ProjectionList<PurchaseItem> items)
        {
            var projection = new Purchase { Id = Mark.Int64, Items = items };
            var stored = (await Db.Purchases.Fetch(new PurchaseCriteria().WithProjection(projection), cancellationToken: Ct)).Single();
            return [.. stored.Items!.Select(i => i.Product!.Name!)];
        }

        var shape = new PurchaseItem { Product = new Product { Name = Mark.String } };

        // amount >= 2, translated by the item repository's conditions inside the purchase's query
        Assert.Equal(["Ball", "Tape"], await Names(Mark.List(shape,
            new PurchaseItemCriteria().Amount.Ge(2).WithOrderBy(PurchaseItemOrderBy.MostExpensiveFirst))));
        Assert.Equal(["Ball"], await Names(Mark.List(shape,
            new PurchaseItemCriteria().Amount.Ge(2).WithOrderBy(PurchaseItemOrderBy.MostExpensiveFirst), limit: 1)));
    }

    // :: Projection of a multivalued field (ProjectionList): criteria, order and range on the children, per parent

    /// <summary>
    /// Ball sold in four purchases (amounts 1, 3, 4, 2) and Tape in three (5, 2, 1); Net never sold. Returns the
    /// products and the purchases, in that order of creation.
    /// </summary>
    private async Task<(Product Ball, Product Tape, Product Net, Purchase[] Purchases)> Sales()
    {
        var ana = await NewUser("ana", "Ana");
        var ball = await NewProduct("Ball", 10m);
        var tape = await NewProduct("Tape", 2m);
        var net = await NewProduct("Net", 50m);
        Purchase[] purchases =
        [
            await NewPurchase(ana, (ball, 1), (tape, 5)),
            await NewPurchase(ana, (ball, 3), (tape, 2)),
            await NewPurchase(ana, (ball, 4), (tape, 1)),
            await NewPurchase(ana, (ball, 2)),
        ];
        return (ball, tape, net, purchases);
    }

    private static readonly PurchaseItem s_amountOnly = new() { Amount = Mark.Int32 };

    private async Task<Dictionary<string, int[]>> ItemsOfEachProduct(ProjectionList<PurchaseItem> items)
    {
        var products = await Db.Products.Fetch(
            new ProductCriteria().WithProjection(new Product { Name = Mark.String, Items = items }).WithOrderBy(ProductOrderBy.NameAToZ),
            cancellationToken: Ct);
        return products.ToDictionary(p => p.Name!, p => p.Items!.Select(i => i.Amount!.Value).ToArray());
    }

    [Fact]
    public async Task MultivaluedField_AProductBringsOnlyTheItemsAsked_Ordered_WithinTheRange()
    {
        await Sales();

        // Items of amount >= 2, largest first, skipping 1 and taking 2 — for each product on its own.
        var items = await ItemsOfEachProduct(Mark.List(s_amountOnly,
            new PurchaseItemCriteria().Amount.Ge(2).WithOrderBy(PurchaseItemOrderBy.LargestQuantityFirst), limit: 2, offset: 1));

        Assert.Equal([3, 2], items["Ball"]); // 3, 4, 2 → 4, 3, 2 → [3, 2]
        Assert.Equal([2], items["Tape"]); // 5, 2 → 5, 2 → [2]
        Assert.Empty(items["Net"]); // never sold: an empty list, not a missing product
    }

    [Fact]
    public async Task MultivaluedField_TheOrderAlone_AndTheCriteriaAlone()
    {
        await Sales();

        var ordered = await ItemsOfEachProduct(Mark.List(s_amountOnly,
            new PurchaseItemCriteria().WithOrderBy(PurchaseItemOrderBy.LargestQuantityFirst)));
        Assert.Equal([4, 3, 2, 1], ordered["Ball"]);
        Assert.Equal([5, 2, 1], ordered["Tape"]);

        var filtered = await ItemsOfEachProduct(Mark.List(s_amountOnly, new PurchaseItemCriteria().Amount.Le(2))); // key order
        Assert.Equal([1, 2], filtered["Ball"]);
        Assert.Equal([2, 1], filtered["Tape"]);

        var rangeOnly = await ItemsOfEachProduct(Mark.List(s_amountOnly,
            new PurchaseItemCriteria().WithOrderBy(PurchaseItemOrderBy.OldestFirst), limit: 2, offset: 2));
        Assert.Equal([4, 2], rangeOnly["Ball"]); // 1, 3, 4, 2 → skip 2 → [4, 2]
        Assert.Equal([1], rangeOnly["Tape"]); // 5, 2, 1 → skip 2 → [1]
    }

    [Fact]
    public async Task MultivaluedField_TheRangeIsPerParent_NotOverAllTheChildren()
    {
        var (_, _, _, purchases) = await Sales();

        var projection = new Purchase
        {
            Id = Mark.Int64,
            Items = Mark.List(s_amountOnly,
                new PurchaseItemCriteria().Amount.Ge(2).WithOrderBy(PurchaseItemOrderBy.LargestQuantityFirst), limit: 1),
        };
        var stored = (await Db.Purchases.Fetch(new PurchaseCriteria().WithProjection(projection), cancellationToken: Ct))
            .ToDictionary(p => p.Id!.Value, p => p.Items!.Select(i => i.Amount!.Value).ToArray());

        // One item per purchase (the largest of amount >= 2), never one item for the whole result.
        Assert.Equal([5], stored[purchases[0].Id!.Value]);
        Assert.Equal([3], stored[purchases[1].Id!.Value]);
        Assert.Equal([4], stored[purchases[2].Id!.Value]);
        Assert.Equal([2], stored[purchases[3].Id!.Value]);
    }

    // :: Writes in cascade, as the projection asks

    private static readonly PurchaseItem s_itemWrite = new() { Amount = Mark.Int32, Price = Mark.Decimal, Product = new Product() };

    private async Task<int[]> AmountsOf(long purchaseId) =>
        [.. (await Db.Items.Fetch(new PurchaseItemCriteria().PurchaseId.Eq(purchaseId).WithOrderBy(PurchaseItemOrderBy.OldestFirst),
            cancellationToken: Ct)).Select(i => i.Amount!.Value)];

    [Fact]
    public async Task Cascade_Insert_WritesTheMarkedCollection_PointingAtTheParent()
    {
        var ana = await NewUser("ana", "Ana");
        var ball = await NewProduct("Ball", 10m);
        var purchase = new Purchase
        {
            User = ana,
            BuyDate = DateTimeOffset.UtcNow,
            Items =
            [
                new PurchaseItem { Product = ball, Amount = 2, Price = 10m },
                new PurchaseItem { Product = ball, Amount = 3, Price = 10m },
            ],
        };

        Assert.True(await Db.Purchases.Insert(purchase,
            new Purchase { User = new User(), BuyDate = Mark.Instant, Items = Mark.List(s_itemWrite) }, Ct));

        Assert.Equal(new[] { 2, 3 }, await AmountsOf(purchase.Id!.Value));
    }

    [Fact]
    public async Task Cascade_Update_TheMarkedCollectionIsItsFullState()
    {
        var ana = await NewUser("ana", "Ana");
        var ball = await NewProduct("Ball", 10m);
        var purchase = await NewPurchase(ana, (ball, 1), (ball, 2), (ball, 3));
        var stored = await Db.Items.Fetch(new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id).WithOrderBy(PurchaseItemOrderBy.OldestFirst),
            cancellationToken: Ct);
        var (a, c) = (stored[0], stored[2]);

        // Untouched when the projection does not mark the collection.
        await Db.Purchases.Update(new Purchase { Id = purchase.Id, BuyDate = DateTimeOffset.UtcNow, Items = [] },
            projection: new Purchase { BuyDate = Mark.Instant }, cancellationToken: Ct);
        Assert.Equal(new[] { 1, 2, 3 }, await AmountsOf(purchase.Id!.Value));

        // Marked: A changes, B is left out (deleted), C stays, D is new (inserted).
        var changed = await Db.Purchases.Update(new Purchase
        {
            Id = purchase.Id,
            Items =
            [
                new PurchaseItem { Id = a.Id, Amount = 10, Price = 10m, Product = ball },
                new PurchaseItem { Id = c.Id, Amount = 3, Price = 10m, Product = ball },
                new PurchaseItem { Amount = 7, Price = 10m, Product = ball },
            ],
        }, projection: new Purchase { Items = Mark.List(s_itemWrite) }, cancellationToken: Ct);

        Assert.True(changed);
        Assert.Equal(new[] { 10, 3, 7 }, await AmountsOf(purchase.Id!.Value));

        // An empty list is the state "no items".
        await Db.Purchases.Update(new Purchase { Id = purchase.Id, Items = [] }, projection: new Purchase { Items = Mark.List(s_itemWrite) },
            cancellationToken: Ct);
        Assert.Empty(await AmountsOf(purchase.Id!.Value));
    }

    [Fact]
    public async Task Cascade_AReferenceMarkedBeyondItsKey_WritesTheReferencedRow_OrInsertsIt()
    {
        var ana = await NewUser("ana", "Ana");
        var ball = await NewProduct("Ball", 10m);
        var purchase = await NewPurchase(ana, (ball, 1));

        // Key only: just the foreign key (nothing of the user is written).
        await Db.Purchases.Update(new Purchase { Id = purchase.Id, User = new User { Id = ana.Id, Name = "ignored" } },
            projection: new Purchase { User = new User() }, cancellationToken: Ct);
        Assert.Equal("Ana", (await Db.Users.FetchById(ana.Id!.Value, cancellationToken: Ct))!.Name);

        // Marked beyond the key: the user's row is updated too.
        await Db.Purchases.Update(new Purchase { Id = purchase.Id, User = new User { Id = ana.Id, Name = "Ana Maria" } },
            projection: new Purchase { User = new User { Name = Mark.String } }, cancellationToken: Ct);
        Assert.Equal("Ana Maria", (await Db.Users.FetchById(ana.Id!.Value, cancellationToken: Ct))!.Name);

        // Without a key: inserted first, and the purchase points at it.
        var bia = new User { UserName = "bia", Name = "Bia", Password = "x" };
        await Db.Purchases.Update(new Purchase { Id = purchase.Id, User = bia },
            projection: new Purchase { User = new User { UserName = Mark.String, Name = Mark.String, Password = Mark.String } },
            cancellationToken: Ct);
        var now = await Db.Purchases.FetchById(purchase.Id!.Value, new Purchase { User = new User { Name = Mark.String } }, Ct);
        Assert.Equal("Bia", now!.User!.Name);
        Assert.NotEqual(ana.Id, now.User.Id);
    }

    [Fact]
    public async Task Cascade_Delete_TakesTheDeclaredCollection_ButNeverAView()
    {
        var ana = await NewUser("ana", "Ana");
        var ball = await NewProduct("Ball", 10m);
        var purchase = await NewPurchase(ana, (ball, 1), (ball, 2));
        var other = await NewPurchase(ana, (ball, 5));

        Assert.Equal(1, await Db.Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(purchase.Id), Ct));

        Assert.Empty(await AmountsOf(purchase.Id!.Value)); // its items went with it
        Assert.Equal(new[] { 5 }, await AmountsOf(other.Id!.Value)); // another purchase's did not

        // The items of a product are a view (no cascade): a sold product cannot be deleted from under its sales.
        await Assert.ThrowsAnyAsync<Exception>(() => Db.Products.Delete(new ProductCriteria().ProductId.Eq(ball.Id), Ct));
        Assert.Equal(new[] { 5 }, await AmountsOf(other.Id!.Value));
    }

    [Fact]
    public async Task Fetch_ValuesRoundTrip_BytesInstantsDecimalsEnums()
    {
        var product = await NewProduct("Image", 1234567.89m);
        await Db.Products.Update(new Product { Id = product.Id, Image = [0, 255, 7, 8] },
            projection: new Product { Image = Mark.Bytes }, cancellationToken: Ct);
        var user = await NewUser("dora", "Dora");
        await Db.Users.Update(new User { Id = user.Id, Role = Role.Admin }, projection: new User { Role = Mark.Enum<Role>() },
            cancellationToken: Ct);
        var when = new DateTimeOffset(2026, 11, 18, 9, 30, 15, 123, TimeSpan.FromHours(-3));
        var purchase = new Purchase { User = user, BuyDate = when };
        await Db.Purchases.Insert(purchase, cancellationToken: Ct);

        var storedProduct = await Db.Products.FetchById(product.Id!.Value, new Product { Image = Mark.Bytes, Price = Mark.Decimal }, Ct);
        var storedUser = await Db.Users.FetchById(user.Id!.Value, cancellationToken: Ct);
        var storedPurchase = await Db.Purchases.FetchById(purchase.Id!.Value, cancellationToken: Ct);

        Assert.Equal([0, 255, 7, 8], storedProduct!.Image!);
        Assert.Equal(1234567.89m, storedProduct.Price);
        Assert.Equal(Role.Admin, storedUser!.Role);
        Assert.Equal(when, storedPurchase!.BuyDate); // same instant, read back in UTC
    }

    // :: Alternative groups and a term over several columns

    [Fact]
    public async Task AnyOf_GroupsJoinWithOr_AndTheOwnFieldsWithAnd()
    {
        Assert.SkipWhen(fixture.IsRemote, "alternative groups do not travel: the refusal has its own test");
        var a = await NewProduct("Group A", 10m);
        await NewProduct("Group B", 20m);
        var c = await NewProduct("Group C", 30m);
        await NewProduct("Other", 10m);

        var criteria = new ProductCriteria().Name.StartingWith("Group ")
            .WithAnyOf(new ProductCriteria().Price.Eq(10m), new ProductCriteria().Price.Eq(30m))
            .WithOrderBy(ProductOrderBy.NameAToZ);

        Assert.Equal([a.Id, c.Id], (await Db.Products.Fetch(criteria, cancellationToken: Ct)).Select(p => p.Id));
        Assert.Equal(2, await Db.Products.Count(criteria, Ct));
    }

    [Fact]
    public async Task AnyOf_AGroupAskingNothing_MatchesEveryRow()
    {
        Assert.SkipWhen(fixture.IsRemote, "alternative groups do not travel: the refusal has its own test");
        await NewProduct("Group A", 10m);
        await NewProduct("Group B", 20m);

        var criteria = new ProductCriteria().Name.StartingWith("Group ")
            .WithAnyOf(new ProductCriteria().Price.Eq(10m), new ProductCriteria());

        Assert.Equal(2, await Db.Products.Count(criteria, Ct));
    }

    [Fact]
    public async Task AnyOf_GroupsNest_AndADeleteTakesOnlyTheRowsTheyMatch()
    {
        Assert.SkipWhen(fixture.IsRemote, "alternative groups do not travel: the refusal has its own test");
        var a = await NewProduct("Group A", 10m);
        await NewProduct("Group B", 20m);
        await NewProduct("Group C", 30m);

        // price >= 15 and (name = "Group C"), or price = 10
        var nested = new ProductCriteria().Price.Ge(15m).WithAnyOf(new ProductCriteria().Name.Eq("Group C"));
        var criteria = new ProductCriteria().WithAnyOf(nested, new ProductCriteria().Price.Eq(10m));

        Assert.Equal(2, await Db.Products.Count(criteria, Ct));
        Assert.Equal(2, await Db.Products.Delete(criteria, Ct));
        Assert.Equal(["Group B"], (await Db.Products.Fetch(new ProductCriteria().Name.StartingWith("Group "),
            cancellationToken: Ct)).Select(p => p.Name));
        Assert.Null(await Db.Products.FetchById(a.Id!.Value, cancellationToken: Ct));
    }

    [Fact]
    public async Task AnyOf_IsRefusedByTheClient_BeforeAnythingTravels()
    {
        Assert.SkipWhen(!fixture.IsRemote, "the refusal is the wire's");

        var criteria = new ProductCriteria().WithAnyOf(new ProductCriteria().Price.Eq(10m));

        await Assert.ThrowsAsync<InvalidRequestException>(() => Db.Products.Fetch(criteria, cancellationToken: Ct));
    }

    [Fact]
    public async Task TranslateAny_LooksForTheTermInEveryColumn()
    {
        var inName = await NewProduct("a needle box", 1m);
        var inDescription = await NewProduct("Kit", 2m, "with a needle");
        await NewProduct("Thread", 3m, "no sharp things");

        var found = await Db.Products.Fetch(new ProductCriteria().Search.Containing("needle"), cancellationToken: Ct);

        Assert.Equal(new[] { inName.Id, inDescription.Id }.Order(), found.Select(p => p.Id).Order());
    }

    [Fact]
    public async Task TranslateAny_RefusesARestrictedCriterion()
    {
        Assert.SkipWhen(fixture.IsRemote, "restrictions are set by the server");

        var criteria = new ProductCriteria();
        criteria.Search.Restrict("needle");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Products.Fetch(criteria, cancellationToken: Ct));
    }

    // :: Criteria

    [Fact]
    public async Task Restriction_HoldsEvenWhenTheRequestOnTheSameFieldIsDisjunctive()
    {
        Assert.SkipWhen(fixture.IsRemote, "restrictions are set by the server: the REST scope tests cover them");
        var ana = await NewUser("ana", "Ana");
        var bia = await NewUser("bia", "Bia");
        var product = await NewProduct("Ball", 10m);
        var mine = await NewPurchase(ana, (product, 1));
        await NewPurchase(bia, (product, 1));

        // What a client may send: "userId": {"or": true, "p": [{"o": "IS_NOT_NULL"}]} — then the server restricts.
        var criteria = new PurchaseCriteria();
        criteria.UserId.Or();
        criteria.UserId.IsNotNull();
        criteria.UserId.Restrict(ana.Id);

        var found = await Db.Purchases.Fetch(criteria, cancellationToken: Ct);

        Assert.Equal([mine.Id], found.Select(p => p.Id));
        Assert.Equal(1, await Db.Purchases.Count(criteria, Ct));
        Assert.Equal(0, await Db.Purchases.Count(new PurchaseCriteria().UserId.Restrict(-1), Ct));
    }

    [Fact]
    public async Task Criteria_EveryOperatorFilters()
    {
        await NewProduct("Ball (volleyball)", 10.5m);
        await NewProduct("Ball (soccer)", 50m);
        await NewProduct("Tape 50% off", 3m);
        await NewProduct("Net", null);

        async Task<string[]> Names(ProductCriteria c) =>
            [.. (await Db.Products.Fetch(c.WithOrderBy(ProductOrderBy.NameAToZ), cancellationToken: Ct)).Select(p => p.Name!)];

        Assert.Equal(["Ball (volleyball)"], await Names(new ProductCriteria().Name.Eq("Ball (volleyball)")));
        Assert.Equal(["Ball (soccer)", "Ball (volleyball)"], await Names(new ProductCriteria().Name.StartingWith("ball")));
        Assert.Equal(["Tape 50% off"], await Names(new ProductCriteria().Name.Containing("50%"))); // % taken literally
        Assert.Equal(["Ball (volleyball)", "Tape 50% off"], await Names(new ProductCriteria().Price.Lt(20m)));
        Assert.Equal(["Ball (soccer)", "Ball (volleyball)"], await Names(new ProductCriteria().Price.Between(10m, 50m)));
        Assert.Equal(["Net"], await Names(new ProductCriteria().Price.IsNull()));
        Assert.Equal(["Ball (soccer)", "Net"], await Names(new ProductCriteria().Name.Or().Eq("Net").Name.Eq("Ball (soccer)")));
        Assert.Empty(await Names(new ProductCriteria().ProductId.In())); // an empty list matches nothing
        Assert.Equal(["Ball (volleyball)"], await Names(new ProductCriteria().Name.StartingWith("Ball").Price.Lt(20m))); // fields: AND
    }

    [Fact]
    public async Task Criteria_OrderingCountAndPages()
    {
        foreach (var (name, price) in new[] { ("a", 5m), ("b", 1m), ("c", 3m), ("d", 4m), ("e", 2m) })
        {
            await NewProduct(name, price);
        }

        var cheapest = new ProductCriteria().WithOrderBy(ProductOrderBy.CheapestFirst);
        Assert.Equal(["b", "e", "c"], (await Db.Products.Fetch(cheapest, limit: 3, cancellationToken: Ct)).Select(p => p.Name));
        Assert.Equal(["c", "d"], (await Db.Products.Fetch(cheapest, offset: 2, limit: 2, cancellationToken: Ct)).Select(p => p.Name));
        Assert.Equal(2, await Db.Products.Count(new ProductCriteria().Price.Ge(4m), Ct));

        var page = await Db.Products.FetchPage(cheapest, page: 1, pageSize: 2, Ct);
        Assert.Equal(["c", "d"], page.Items.Select(p => p.Name));
        Assert.Equal((1, 3, 5), (page.Number, page.TotalPages, page.TotalItems));
    }

    [Fact]
    public async Task Criteria_AVirtualField_FiltersThroughAnotherTable()
    {
        var user = await NewUser("eva", "Eva");
        var ball = await NewProduct("Ball", 10m);
        var net = await NewProduct("Net", 20m);
        var withBall = await NewPurchase(user, (ball, 1));
        await NewPurchase(user, (net, 1));

        var found = await Db.Purchases.Fetch(new PurchaseCriteria().ProductId.Eq(ball.Id), cancellationToken: Ct);

        Assert.Equal([withBall.Id], found.Select(p => p.Id));
    }

    [Fact]
    public async Task Criteria_TheRepositoryDoesNotTranslate_IsAConfigurationError_NotIgnored()
    {
        Assert.SkipWhen(fixture.IsRemote, "a mapping matter of the SQL side");
        var database = fixture.Shopping!.Database;

        // Another criteria type than the one the repository translates.
        var users = new SqlRepository<User, UserCriteriaWithUnmappedField, long>(database, UserRepository.Mapping);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            users.Fetch(new UserCriteriaWithUnmappedField().Nickname.Eq("x"), cancellationToken: Ct));

        // A field the conditions leave out: an error naming it, never a filter silently dropped (or a restriction).
        var partial = EntityMapping.For(User.Model, "user_account", "u")
            .Key("id", generated: true)
            .Column("userName", "user_name")
            .Conditions<UserCriteria>((c, t) => t.Translate("user_name", c.UserName))
            .Build();
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlRepository<User, UserCriteria, long>(database, partial).Count(new UserCriteria().Role.Restrict(Role.Admin), Ct));
        Assert.Contains("'role'", caught.Message, StringComparison.Ordinal);
    }

    // :: Update and delete

    [Fact]
    public async Task Update_WithProjection_WritesOnlyTheMarkedFields_AndANullClearsTheColumn()
    {
        var product = await NewProduct("Ball", 10m, "round");

        var changed = await Db.Products.Update(new Product { Id = product.Id, Price = 12m, Description = null, Name = "ignored" },
            projection: new Product { Price = Mark.Decimal, Description = Mark.String }, cancellationToken: Ct);

        var stored = await Db.Products.FetchById(product.Id!.Value, cancellationToken: Ct);
        Assert.True(changed);
        Assert.Equal(("Ball", 12m, (string?)null), (stored!.Name, stored.Price, stored.Description));
    }

    [Fact]
    public async Task Update_WithNeitherProjectionNorOldBean_WritesWhatHoldsAValue_AndLeavesTheRest()
    {
        var product = await NewProduct("Ball", 10m, "round");
        var narrow = (await Db.Products.FetchById(product.Id!.Value, new Product { Name = Mark.String }, Ct))!;
        Assert.Null(narrow.Price); // read without it

        narrow.Name = "New ball";
        Assert.True(await Db.Products.Update(narrow, cancellationToken: Ct));

        var stored = await Db.Products.FetchById(product.Id!.Value, cancellationToken: Ct);
        Assert.Equal(("New ball", 10m, "round"), (stored!.Name, stored.Price, stored.Description));
    }

    [Fact]
    public async Task Update_WithTheOldBean_WritesOnlyWhatChanged_AndNothingChangedIsFalse()
    {
        var product = await NewProduct("Ball", 10m, "round");
        var before = (await Db.Products.FetchById(product.Id!.Value, cancellationToken: Ct))!;
        var after = new Product { Id = before.Id, Name = before.Name, Price = 11m, Description = before.Description };

        Assert.True(await Db.Products.Update(after, before, cancellationToken: Ct));
        Assert.False(await Db.Products.Update(after, after, cancellationToken: Ct));
        Assert.Equal(11m, (await Db.Products.FetchById(product.Id!.Value, cancellationToken: Ct))!.Price);
    }

    [Fact]
    public async Task Update_AReference_WritesTheForeignKey()
    {
        var ana = await NewUser("ana2", "Ana");
        var bia = await NewUser("bia2", "Bia");
        var purchase = new Purchase { User = ana };
        await Db.Purchases.Insert(purchase, cancellationToken: Ct);

        await Db.Purchases.Update(new Purchase { Id = purchase.Id, User = bia }, projection: new Purchase { User = new User() },
            cancellationToken: Ct);

        Assert.Equal(bia.Id, (await Db.Purchases.FetchById(purchase.Id!.Value, cancellationToken: Ct))!.User!.Id);
    }

    [Fact]
    public async Task Delete_RemovesWhatMatches_AndRefusesAnEmptyFilter()
    {
        await NewProduct("a", 1m);
        await NewProduct("b", 2m);

        await Assert.ThrowsAsync<InvalidRequestException>(() => Db.Products.Delete(new ProductCriteria(), Ct));
        Assert.Equal(1, await Db.Products.Delete(new ProductCriteria().Name.Eq("a"), Ct));
        Assert.Equal(1, await Db.Products.Count(new ProductCriteria().Name.IsNotNull(), Ct));
        Assert.Null(await Db.Products.FetchById(-1, cancellationToken: Ct));
    }

    // :: Transactions

    [Fact]
    public async Task Transaction_AnExceptionRollsBackEverything_AndSurfacesUnchanged()
    {
        var error = new InvalidOperationException("boom");

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Tx.Required(async _ =>
        {
            await NewProduct("a", 1m);
            await NewProduct("b", 2m);
            throw error;
        }, Ct));

        Assert.Same(error, caught);
        Assert.Equal(0, await Db.Products.Count(new ProductCriteria().Name.IsNotNull(), Ct));
    }

    [Fact]
    public async Task Transaction_RollbackOnly_UndoesWithoutThrowing()
    {
        await Db.Tx.Required(async tx =>
        {
            await NewProduct("a", 1m);
            tx.SetRollbackOnly();
        }, Ct);

        Assert.Equal(0, await Db.Products.Count(new ProductCriteria().Name.IsNotNull(), Ct));
    }

    [Fact]
    public async Task Transaction_WriteWithoutATransaction_RunsInOneOfItsOwn()
    {
        Assert.False(Db.InTransaction);

        await NewProduct("solo", 1m); // implicit REQUIRED: committed

        Assert.Equal(1, await Db.Products.Count(new ProductCriteria().Name.Eq("solo"), Ct));
    }

    [Fact]
    public async Task Transaction_AWriteThatFailsHalfway_LeavesNothingBehind()
    {
        Assert.SkipWhen(fixture.IsRemote, "a statement-level matter of the SQL side");
        // Insert is two statements (INSERT, then read the row back). Here the read-back hits a column that does not
        // exist: inside the implicit REQUIRED the INSERT is undone; in autocommit it would stay, orphaned.
        var broken = EntityMapping.For(Product.Model, "product", "p")
            .Key("id", generated: true)
            .Column("name")
            .Column("description", "ghost_column")
            .Build();
        var products = new SqlRepository<Product, ProductCriteria, long>(fixture.Shopping!.Database, broken);

        await Assert.ThrowsAnyAsync<Exception>(() => products.Insert(new Product { Name = "half" }, cancellationToken: Ct));

        Assert.Equal(0, await Db.Products.Count(new ProductCriteria().Name.Eq("half"), Ct));
    }

    [Fact]
    public async Task Transaction_UncommittedWork_IsInvisibleOutside_AndVisibleInside()
    {
        // Reading outside while the transaction holds its locks: a locking database (SQL Server's default) waits.
        Assert.SkipUnless(await fixture.ReadsDoNotWaitForWriters(),
            "the database blocks readers on uncommitted rows (enable READ_COMMITTED_SNAPSHOT to run this test)");

        await Db.Tx.Required(async _ =>
        {
            await NewProduct("pending", 1m);

            var inside = await Db.Products.Count(new ProductCriteria().Name.Eq("pending"), Ct);
            var outside = await Db.Tx.NotSupported(_ => Db.Products.Count(new ProductCriteria().Name.Eq("pending"), Ct), Ct);

            Assert.Equal((1, 0), (inside, outside));
        }, Ct);
    }

    [Fact]
    public async Task Transaction_RequiresNew_CommitsEvenWhenTheOuterOneRollsBack()
    {
        await Db.Tx.Required(async tx =>
        {
            await NewProduct("outer", 1m);
            await Db.Tx.RequiresNew(_ => NewProduct("inner", 2m), Ct);
            tx.SetRollbackOnly();
        }, Ct);

        Assert.Equal(["inner"], (await Db.Products.Fetch(new ProductCriteria().Name.IsNotNull(), cancellationToken: Ct)).Select(p => p.Name));
    }

    [Fact]
    public async Task Transaction_AParticipantJoins_AndItsRollbackOnlyUndoesTheWhole()
    {
        await Db.Tx.Required(async _ =>
        {
            await NewProduct("one", 1m);
            await Db.Tx.Required(async inner =>
            {
                Assert.True(inner.IsActive);
                await NewProduct("two", 2m);
                inner.SetRollbackOnly();
            }, Ct);
        }, Ct);

        Assert.Equal(0, await Db.Products.Count(new ProductCriteria().Name.IsNotNull(), Ct));
    }

    [Fact]
    public async Task Transaction_AParticipantThatFails_UndoesTheWhole_EvenWhenItsCallerCatches()
    {
        await Assert.ThrowsAsync<TransactionSystemException>(() => Db.Tx.Required(async tx =>
        {
            await NewProduct("one", 1m);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Tx.Required<int>(async _ =>
            {
                await NewProduct("two", 2m);
                throw new InvalidOperationException("boom");
            }, Ct));

            Assert.True(tx.IsRollbackOnly);
        }, Ct)); // not a success: what the work wrote is gone

        Assert.Equal(0, await Db.Products.Count(new ProductCriteria().Name.IsNotNull(), Ct));
    }

    [Fact]
    public async Task Detached_TwoRunsAtOnce_TheSecondIsRefused_AndTheCommitWaitsForNone()
    {
        Assert.SkipWhen(fixture.IsRemote, "the detached transaction of the SQL side");
        await using var tx = await fixture.Shopping!.Database.BeginDetached(Ct);
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = tx.Run(async () =>
        {
            await NewProduct("first", 1m);
            inside.SetResult();
            await release.Task;
            return 1;
        });
        await inside.Task;

        await Assert.ThrowsAsync<TransactionConflictException>(() => tx.Run(() => Task.FromResult(2)));
        await Assert.ThrowsAsync<TransactionConflictException>(() => tx.Commit(Ct));

        release.SetResult();
        Assert.Equal(1, await first);
        Assert.True(await tx.Commit(Ct));
        Assert.Equal(1, await Db.Products.Count(new ProductCriteria().Name.Eq("first"), Ct));
    }

    [Fact]
    public async Task Detached_ARollbackAskedDuringARun_HappensWhenTheRunEnds()
    {
        Assert.SkipWhen(fixture.IsRemote, "the detached transaction of the SQL side");
        var tx = await fixture.Shopping!.Database.BeginDetached(Ct);
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var run = tx.Run(async () =>
        {
            await NewProduct("doomed", 1m);
            inside.SetResult();
            await release.Task;
            await NewProduct("still-inside", 2m); // the connection is still the transaction's
            return 1;
        });
        await inside.Task;

        await tx.Rollback(Ct);
        release.SetResult();
        await run;

        await Assert.ThrowsAsync<TransactionConflictException>(() => tx.Run(() => Task.FromResult(2)));
        Assert.Equal(0, await Db.Products.Count(new ProductCriteria().Name.IsNotNull(), Ct));
    }

    [Fact]
    public async Task Detached_ARunThatFails_LeavesNothingToCommit()
    {
        Assert.SkipWhen(fixture.IsRemote, "the detached transaction of the SQL side");
        await using var tx = await fixture.Shopping!.Database.BeginDetached(Ct);
        await tx.Run(() => NewProduct("kept?", 1m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.Run<int>(async () =>
        {
            await NewProduct("half", 2m);
            throw new InvalidOperationException("boom");
        }));

        Assert.False(await tx.Commit(Ct));
        Assert.Equal(0, await Db.Products.Count(new ProductCriteria().Name.IsNotNull(), Ct));
    }

    [Fact]
    public async Task Transaction_AfterCommit_RunsAfterCommit_IsDroppedOnRollback_AndRunsAtOnceWithoutATransaction()
    {
        var log = new List<string>();

        await Db.Tx.Required(async _ =>
        {
            await Db.Tx.AfterCommit(() =>
            {
                log.Add("committed");
                return Task.CompletedTask;
            });
            log.Add("work");
        }, Ct);

        await Db.Tx.Required(async tx =>
        {
            await Db.Tx.AfterCommit(() =>
            {
                log.Add("never");
                return Task.CompletedTask;
            });
            tx.SetRollbackOnly();
        }, Ct);

        await Db.Tx.AfterCommit(() =>
        {
            log.Add("now");
            return Task.CompletedTask;
        });

        Assert.Equal(["work", "committed", "now"], log);
    }

    [Fact]
    public async Task Transaction_MandatoryAndNever_EnforceWhatTheySay()
    {
        await Assert.ThrowsAsync<TransactionSystemException>(() => Db.Tx.Mandatory(_ => Task.FromResult(1), Ct));
        await Db.Tx.Required(async _ =>
            await Assert.ThrowsAsync<TransactionSystemException>(() => Db.Tx.Never(_ => Task.FromResult(1), Ct)), Ct);
    }
}
