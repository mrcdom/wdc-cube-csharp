using System.Globalization;
using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>
/// The repository seen by whoever is acting: the policy's rules apply in process, with no transport,
/// and to the children written in cascade. On PostgreSQL.
/// </summary>
public sealed class ProtectedRepositoryTests(PostgreSqlFixture fixture) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Shopping Local => fixture.Shopping!;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(fixture.Shopping is null, fixture.SkipReason);
        await fixture.Reset();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // :: The protected side of one session

    private sealed record Side(
        ActorSource Actors,
        ProtectedRepository<Purchase, PurchaseCriteria, long> Purchases,
        ProtectedRepository<PurchaseItem, PurchaseItemCriteria, long> Items,
        ProtectedRepository<User, UserCriteria, long> Users)
    {
        public Side As(User user, params string[] roles)
        {
            Actors.Current = new Actor(user.Id!.Value.ToString(CultureInfo.InvariantCulture), roles);
            return this;
        }
    }

    private static long IdOf(PolicyContext p) => long.Parse(p.Actor.Id, CultureInfo.InvariantCulture);

    /// <summary>Purchases reach only their buyer's; deleting them needs the "keeper" role.</summary>
    private static RepositoryPolicy<Purchase, PurchaseCriteria, long> PurchasesOfTheBuyer() =>
        new(Purchase.Model, () => new PurchaseCriteria())
        {
            Authorize = (p, operation) =>
            {
                if (operation == RepositoryOperation.Delete && !p.Actor.HasRole("keeper"))
                {
                    throw new AccessDeniedException("Only a keeper deletes purchases.");
                }
            },
            RestrictToId = (c, id) => c.PurchaseId.Restrict(id),
            Scope = (p, c) => c.UserId.Restrict(IdOf(p)),
        };

    private static RepositoryPolicy<PurchaseItem, PurchaseItemCriteria, long> FreeItems() =>
        new(PurchaseItem.Model, () => new PurchaseItemCriteria()) { RestrictToId = (c, id) => c.PurchaseItemId.Restrict(id) };

    /// <summary>Users never show their password; each reads only themselves.</summary>
    private static RepositoryPolicy<User, UserCriteria, long> UsersWithoutPassword() =>
        new(User.Model, () => new UserCriteria())
        {
            SanitizeProjection = (_, u) => u.Password = null,
            SanitizeOutput = (_, u) => u.Password = null,
        };

    private Side Protect(RepositoryPolicy<Purchase, PurchaseCriteria, long>? purchases = null,
        RepositoryPolicy<PurchaseItem, PurchaseItemCriteria, long>? items = null, bool protectItems = true)
    {
        var actors = new ActorSource();
        var protection = new Protection(actors, Local.Database);
        var purchaseSide = new ProtectedRepository<Purchase, PurchaseCriteria, long>(Local.Purchases,
            purchases ?? PurchasesOfTheBuyer(), protection);
        var itemSide = protectItems
            ? new ProtectedRepository<PurchaseItem, PurchaseItemCriteria, long>(Local.Items, items ?? FreeItems(), protection)
            : new ProtectedRepository<PurchaseItem, PurchaseItemCriteria, long>(Local.Items, items ?? FreeItems(),
                new Protection(actors, Local.Database)); // in another session: not this one's
        var userSide = new ProtectedRepository<User, UserCriteria, long>(Local.Users, UsersWithoutPassword(), protection);
        return new Side(actors, purchaseSide, itemSide, userSide);
    }

    private async Task<User> NewUser(string name)
    {
        var user = new User { UserName = name, Name = name, Password = "secret-" + name };
        await Local.Users.Insert(user, cancellationToken: Ct);
        return user;
    }

    private async Task<Product> NewProduct()
    {
        var product = new Product { Name = "Bola", Price = 10m };
        await Local.Products.Insert(product, cancellationToken: Ct);
        return product;
    }

    private async Task<Purchase> NewPurchase(User buyer, Product product, params int[] amounts)
    {
        var purchase = new Purchase
        {
            User = buyer,
            BuyDate = DateTimeOffset.UtcNow,
            Items = [.. amounts.Select(a => new PurchaseItem { Product = product, Amount = a, Price = 10m })],
        };
        await Local.Purchases.Insert(purchase, cancellationToken: Ct);
        return purchase;
    }

    private Task<int> ItemsOf(Purchase purchase) => Local.Items.Count(new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id), Ct);

    // :: Who is acting

    [Fact]
    public async Task Nobody_IsServedNothing()
    {
        var side = Protect();

        await Assert.ThrowsAsync<AuthenticationException>(() => side.Purchases.Count(new PurchaseCriteria(), Ct));
        await Assert.ThrowsAsync<AuthenticationException>(() => side.Purchases.Insert(new Purchase(), cancellationToken: Ct));
    }

    [Fact]
    public async Task AnOperationTheActorMayNotDo_IsRefused_AndNothingIsDeleted()
    {
        var ana = await NewUser("ana");
        var purchase = await NewPurchase(ana, await NewProduct(), 1);
        var side = Protect().As(ana);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            side.Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(purchase.Id), Ct));
        Assert.Equal(1, await side.As(ana, "keeper").Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(purchase.Id), Ct));
    }

    // :: The actor's reach

    [Fact]
    public async Task Reach_ADisjunctiveCriteriaDoesNotWidenIt()
    {
        var (ana, bia) = (await NewUser("ana"), await NewUser("bia"));
        var product = await NewProduct();
        var ofAna = await NewPurchase(ana, product, 1);
        await NewPurchase(bia, product, 1);
        var side = Protect().As(ana);
        var criteria = new PurchaseCriteria();
        criteria.UserId.Or();
        criteria.UserId.IsNotNull();

        Assert.Equal([ofAna.Id], (await side.Purchases.Fetch(criteria, cancellationToken: Ct)).Select(p => p.Id));
        Assert.Equal(1, await side.Purchases.Count(new PurchaseCriteria(), Ct));
        Assert.Equal(1, (await side.Purchases.FetchPage(new PurchaseCriteria(), 0, 10, Ct)).TotalItems);
    }

    [Fact]
    public async Task Reach_GuardsFetchByIdUpdateAndDelete()
    {
        var (ana, bia) = (await NewUser("ana"), await NewUser("bia"));
        var ofBia = await NewPurchase(bia, await NewProduct(), 1);
        var side = Protect().As(ana, "keeper");

        Assert.Null(await side.Purchases.FetchById(ofBia.Id!.Value, cancellationToken: Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            side.Purchases.Update(new Purchase { Id = ofBia.Id, BuyDate = DateTimeOffset.UnixEpoch },
                projection: new Purchase { BuyDate = Mark.Instant }, cancellationToken: Ct));
        Assert.Equal(0, await side.Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(ofBia.Id), Ct));
        await Assert.ThrowsAsync<InvalidRequestException>(() => side.Purchases.Delete(new PurchaseCriteria(), Ct)); // the reach is no filter

        var stored = await Local.Purchases.FetchById(ofBia.Id.Value, new Purchase { BuyDate = Mark.Instant }, Ct);
        Assert.NotEqual(DateTimeOffset.UnixEpoch, stored!.BuyDate);
    }

    // :: The fields the actor reads

    [Fact]
    public async Task Fields_WhatTheActorMayNotReadNeverComesBack()
    {
        var ana = await NewUser("ana");
        var side = Protect().As(ana);
        var withPassword = new User { Id = Mark.Int64, Name = Mark.String, Password = Mark.String };

        var fetched = Assert.Single(await side.Users.Fetch(new UserCriteria().WithProjection(withPassword), cancellationToken: Ct));
        var byId = await side.Users.FetchById(ana.Id!.Value, new User { Password = Mark.String }, Ct);
        var inserted = new User { UserName = "bia", Name = "Bia", Password = "secret" };
        await side.Users.Insert(inserted, cancellationToken: Ct);

        Assert.Null(fetched.Password);
        Assert.Null(byId!.Password);
        Assert.Null(inserted.Password); // the bean that comes back
        Assert.Equal("ana", fetched.Name);
    }

    // :: The checks of each write

    [Fact]
    public async Task Update_TheCheckSeesTheFieldsWritten_FromTheProjectionOrFromWhatChanged()
    {
        var ana = await NewUser("ana");
        var purchase = await NewPurchase(ana, await NewProduct(), 1);
        var seen = new List<bool>();
        var policy = PurchasesOfTheBuyer();
        policy.BeforeUpdate = (_, _, written, _) =>
        {
            seen.Add(written.BuyDate is not null);
            return ValueTask.CompletedTask;
        };
        var side = Protect(purchases: policy).As(ana);
        var old = await Local.Purchases.FetchById(purchase.Id!.Value, cancellationToken: Ct);

        await side.Purchases.Update(new Purchase { Id = purchase.Id, BuyDate = DateTimeOffset.UnixEpoch },
            projection: new Purchase { BuyDate = Mark.Instant }, cancellationToken: Ct);
        var current = new Purchase { Id = purchase.Id, BuyDate = DateTimeOffset.UnixEpoch, User = old!.User };
        await side.Purchases.Update(new Purchase { Id = purchase.Id, BuyDate = old.BuyDate, User = old.User }, current,
            cancellationToken: Ct); // the date back, nothing else changed

        Assert.Equal([true, true], seen);
    }

    [Fact]
    public async Task Insert_ARefusalInsideRollsBackEverything()
    {
        var ana = await NewUser("ana");
        var policy = PurchasesOfTheBuyer();
        policy.BeforeInsert = (_, p, _) => p.BuyDate is null ? throw new InvalidRequestException("no date") : ValueTask.CompletedTask;
        var side = Protect(purchases: policy).As(ana);

        await Assert.ThrowsAsync<InvalidRequestException>(() =>
            side.Purchases.Insert(new Purchase { User = ana }, cancellationToken: Ct));

        Assert.Equal(0, await Local.Purchases.Count(new PurchaseCriteria(), Ct));
    }

    // :: Children written in cascade answer to their own policy

    [Fact]
    public async Task Cascade_TheChildsPolicyRuns_AndKnowsItIsInCascade()
    {
        var ana = await NewUser("ana");
        var product = await NewProduct();
        var cascades = new List<bool>();
        var items = FreeItems();
        items.BeforeInsert = (p, item, _) =>
        {
            cascades.Add(p.InCascade);
            item.Price = 42m; // completed by the child's rule
            return ValueTask.CompletedTask;
        };
        var side = Protect(items: items).As(ana);
        var purchase = new Purchase
        {
            User = ana,
            BuyDate = DateTimeOffset.UtcNow,
            Items = [new PurchaseItem { Product = product, Amount = 1 }, new PurchaseItem { Product = product, Amount = 2 }],
        };

        await side.Purchases.Insert(purchase, cancellationToken: Ct);
        await side.Items.Insert(new PurchaseItem { Purchase = purchase, Product = product, Amount = 3 }, cancellationToken: Ct);

        Assert.Equal([true, true, false], cascades);
        var prices = await Local.Items.Fetch(new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id)
            .WithProjection(new PurchaseItem { Price = Mark.Decimal }), cancellationToken: Ct);
        Assert.All(prices, i => Assert.Equal(42m, i.Price));
    }

    [Fact]
    public async Task Cascade_TheChildsRefusal_RollsBackTheParent()
    {
        var ana = await NewUser("ana");
        var product = await NewProduct();
        var items = FreeItems();
        items.Authorize = (_, operation) =>
        {
            if (operation == RepositoryOperation.Insert)
            {
                throw new AccessDeniedException("No items.");
            }
        };
        var side = Protect(items: items).As(ana);

        await Assert.ThrowsAsync<AccessDeniedException>(() => side.Purchases.Insert(new Purchase
        {
            User = ana,
            BuyDate = DateTimeOffset.UtcNow,
            Items = [new PurchaseItem { Product = product, Amount = 1, Price = 1m }],
        }, cancellationToken: Ct));

        Assert.Equal(0, await Local.Purchases.Count(new PurchaseCriteria(), Ct));
    }

    [Fact]
    public async Task Cascade_AnOrphanGoesThroughTheChildsDeleteCheck_WithItsOwnCriteria()
    {
        var ana = await NewUser("ana");
        var purchase = await NewPurchase(ana, await NewProduct(), 1, 2);
        var kept = (await Local.Items.Fetch(new PurchaseItemCriteria().PurchaseId.Eq(purchase.Id).Amount.Eq(1), cancellationToken: Ct))[0];
        var items = FreeItems();
        items.BeforeDelete = async (p, criteria, ct) =>
        {
            Assert.True(p.InCascade);
            if (await Local.Items.Count(criteria.Amount.Ge(2), ct) > 0)
            {
                throw new AccessDeniedException("Two or more stay.");
            }
        };
        var side = Protect(items: items).As(ana);

        await Assert.ThrowsAsync<AccessDeniedException>(() => side.Purchases.Update(
            new Purchase { Id = purchase.Id, Items = [new PurchaseItem { Id = kept.Id, Amount = 1 }] },
            projection: new Purchase { Items = Mark.List(new PurchaseItem { Amount = Mark.Int32 }) }, cancellationToken: Ct));

        Assert.Equal(2, await ItemsOf(purchase));
    }

    [Fact]
    public async Task Cascade_AChildNotProtectedInTheSession_CannotBeWrittenThroughItsParent()
    {
        var ana = await NewUser("ana");
        var product = await NewProduct();
        var side = Protect(protectItems: false).As(ana);

        await Assert.ThrowsAsync<AccessDeniedException>(() => side.Purchases.Insert(new Purchase
        {
            User = ana,
            BuyDate = DateTimeOffset.UtcNow,
            Items = [new PurchaseItem { Product = product, Amount = 1, Price = 1m }],
        }, cancellationToken: Ct));

        Assert.Equal(0, await Local.Purchases.Count(new PurchaseCriteria(), Ct));
    }

    [Fact]
    public async Task Cascade_TheRawRepositoryStaysFree_AfterAProtectedWrite()
    {
        var ana = await NewUser("ana");
        var product = await NewProduct();
        var items = FreeItems();
        items.BeforeInsert = (_, _, _) => throw new AccessDeniedException("No items here.");
        var side = Protect(items: items).As(ana);
        await side.Purchases.Insert(new Purchase { User = ana, BuyDate = DateTimeOffset.UtcNow }, cancellationToken: Ct);

        var raw = await NewPurchase(ana, product, 1); // the trusted inside: no policy, no guard left behind

        Assert.Equal(1, await ItemsOf(raw));
    }

    [Fact]
    public async Task Cascade_WhatARuleWritesThroughTheRawStore_StaysTrusted()
    {
        var ana = await NewUser("ana");
        var product = await NewProduct();
        var items = FreeItems();
        items.BeforeInsert = (_, _, _) => throw new AccessDeniedException("No items for the actor.");
        var purchases = PurchasesOfTheBuyer();
        Purchase? written = null;
        purchases.BeforeInsert = async (_, _, ct) => written = await NewPurchase(ana, product, 3); // a correction
        var side = Protect(purchases, items).As(ana);

        await side.Purchases.Insert(new Purchase { User = ana, BuyDate = DateTimeOffset.UtcNow }, cancellationToken: Ct);

        Assert.Equal(1, await ItemsOf(written!));
    }

    [Fact]
    public async Task Cascade_WhatAChildsRuleWritesThroughTheRawStore_StaysTrusted()
    {
        var ana = await NewUser("ana");
        var product = await NewProduct();
        var items = FreeItems();
        var checks = 0;
        Purchase? written = null;
        items.BeforeInsert = async (p, _, ct) =>
        {
            if (++checks > 1)
            {
                throw new AccessDeniedException("Checked again: the raw write was guarded.");
            }

            written = await NewPurchase(ana, product, 4);
        };
        var side = Protect(items: items).As(ana);

        await side.Purchases.Insert(new Purchase
        {
            User = ana,
            BuyDate = DateTimeOffset.UtcNow,
            Items = [new PurchaseItem { Product = product, Amount = 1, Price = 1m }],
        }, cancellationToken: Ct);

        Assert.Equal(1, checks);
        Assert.Equal(1, await ItemsOf(written!));
        Assert.Equal(2, await Local.Purchases.Count(new PurchaseCriteria(), Ct));
    }
}
