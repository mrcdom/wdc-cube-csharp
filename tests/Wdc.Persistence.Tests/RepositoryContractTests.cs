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
        var product = new Product { Name = "Bola", Price = 10m, Description = "not written" };

        await Db.Products.Insert(product, new Product { Name = Mark.String, Price = Mark.Decimal }, Ct);

        var stored = await Db.Products.FetchById(product.Id!.Value, cancellationToken: Ct);
        Assert.Null(stored!.Description);
        Assert.Null(product.Description); // the bean comes back as stored
    }

    [Fact]
    public async Task Fetch_DefaultProjection_OwnFieldsAndReferenceKeys_NoBlobsSecretsOrCollections()
    {
        var user = await NewUser("bia", "Bia");
        var product = await NewProduct("Bola", 10m);
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
        var cheap = await NewProduct("Fita", 2m);
        var mid = await NewProduct("Bola", 10m);
        var dear = await NewProduct("Raquete", 99.9m);
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
        Assert.Equal("Raquete", item.Product!.Name);
    }

    [Fact]
    public async Task Fetch_ValuesRoundTrip_BytesInstantsDecimalsEnums()
    {
        var product = await NewProduct("Imagem", 1234567.89m);
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

    // :: Criteria

    [Fact]
    public async Task Restriction_HoldsEvenWhenTheRequestOnTheSameFieldIsDisjunctive()
    {
        Assert.SkipWhen(fixture.IsRemote, "restrictions are set by the server: the REST scope tests cover them");
        var ana = await NewUser("ana", "Ana");
        var bia = await NewUser("bia", "Bia");
        var product = await NewProduct("Bola", 10m);
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
        await NewProduct("Bola de vôlei", 10.5m);
        await NewProduct("Bola de futebol", 50m);
        await NewProduct("Fita 50% off", 3m);
        await NewProduct("Rede", null);

        async Task<string[]> Names(ProductCriteria c) =>
            [.. (await Db.Products.Fetch(c.WithOrderBy(ProductOrderBy.NameAToZ), cancellationToken: Ct)).Select(p => p.Name!)];

        Assert.Equal(["Bola de vôlei"], await Names(new ProductCriteria().Name.Eq("Bola de vôlei")));
        Assert.Equal(["Bola de futebol", "Bola de vôlei"], await Names(new ProductCriteria().Name.StartingWith("bola")));
        Assert.Equal(["Fita 50% off"], await Names(new ProductCriteria().Name.Containing("50%"))); // % taken literally
        Assert.Equal(["Bola de vôlei", "Fita 50% off"], await Names(new ProductCriteria().Price.Lt(20m)));
        Assert.Equal(["Bola de futebol", "Bola de vôlei"], await Names(new ProductCriteria().Price.Between(10m, 50m)));
        Assert.Equal(["Rede"], await Names(new ProductCriteria().Price.IsNull()));
        Assert.Equal(["Bola de futebol", "Rede"], await Names(new ProductCriteria().Name.Or().Eq("Rede").Name.Eq("Bola de futebol")));
        Assert.Empty(await Names(new ProductCriteria().ProductId.In())); // an empty list matches nothing
        Assert.Equal(["Bola de vôlei"], await Names(new ProductCriteria().Name.StartingWith("Bola").Price.Lt(20m))); // fields: AND
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
        var ball = await NewProduct("Bola", 10m);
        var net = await NewProduct("Rede", 20m);
        var withBall = await NewPurchase(user, (ball, 1));
        await NewPurchase(user, (net, 1));

        var found = await Db.Purchases.Fetch(new PurchaseCriteria().ProductId.Eq(ball.Id), cancellationToken: Ct);

        Assert.Equal([withBall.Id], found.Select(p => p.Id));
    }

    [Fact]
    public async Task Criteria_WithoutAFilterInTheMapping_IsAConfigurationError()
    {
        Assert.SkipWhen(fixture.IsRemote, "a mapping matter of the SQL side");
        var users = new SqlRepository<User, UserCriteriaWithUnmappedField, long>(fixture.Shopping!.Database, ShoppingMappings.User);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            users.Fetch(new UserCriteriaWithUnmappedField().Nickname.Eq("x"), cancellationToken: Ct));
    }

    // :: Update and delete

    [Fact]
    public async Task Update_WithProjection_WritesOnlyTheMarkedFields_AndANullClearsTheColumn()
    {
        var product = await NewProduct("Bola", 10m, "redonda");

        var changed = await Db.Products.Update(new Product { Id = product.Id, Price = 12m, Description = null, Name = "ignored" },
            projection: new Product { Price = Mark.Decimal, Description = Mark.String }, cancellationToken: Ct);

        var stored = await Db.Products.FetchById(product.Id!.Value, cancellationToken: Ct);
        Assert.True(changed);
        Assert.Equal(("Bola", 12m, (string?)null), (stored!.Name, stored.Price, stored.Description));
    }

    [Fact]
    public async Task Update_WithTheOldBean_WritesOnlyWhatChanged_AndNothingChangedIsFalse()
    {
        var product = await NewProduct("Bola", 10m, "redonda");
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

/// <summary>A criteria with a field the user mapping does not know.</summary>
public sealed class UserCriteriaWithUnmappedField : Criteria<UserCriteriaWithUnmappedField, User>
{
    public UserCriteriaWithUnmappedField() => Nickname = Text("nickname");

    public TextCriterion<UserCriteriaWithUnmappedField> Nickname { get; }
}

public sealed class PostgreSqlRepositoryTests(PostgreSqlFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<PostgreSqlFixture>;

public sealed class SqlServerRepositoryTests(SqlServerFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<SqlServerFixture>;

/// <summary>The same contract through the REST routes (decision D-17/D-18): remote repositories and transactions.</summary>
public sealed class PostgreSqlHttpRepositoryTests(PostgreSqlHttpFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<PostgreSqlHttpFixture>;

public sealed class SqlServerHttpRepositoryTests(SqlServerHttpFixture fixture)
    : RepositoryContractTests(fixture), IClassFixture<SqlServerHttpFixture>;
