using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>
/// The errata of a purchase already made, in process and through HTTP: Admin and Manager reduce or
/// remove items, with a reason, and the record says who, when and what each item went from and to.
/// </summary>
public abstract class ErrataTestsBase(ShoppingDatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ShoppingRepositories Store => db.Store!.Repositories;

    private async Task<(ShoppingRepositories Repositories, long UserId)> As(string user)
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var (repositories, authentication) = db.Connect();
        var subject = await authentication.Login(user, user, Ct);
        Assert.NotNull(subject);
        return (repositories, subject.Id);
    }

    /// <summary>A purchase made by fulano, one item per amount, each of a different product.</summary>
    private async Task<(Purchase Purchase, IReadOnlyList<PurchaseItem> Items)> FulanoBuys(params int[] amounts)
    {
        var (fulano, _) = await As("fulano");
        var products = await Store.Products.Fetch(new ProductCriteria().WithOrderBy(ProductOrderBy.OldestFirst),
            cancellationToken: Ct);
        var purchase = new Purchase
        {
            BuyDate = DateTimeOffset.UtcNow,
            Items = [.. amounts.Select((a, i) => new PurchaseItem { Product = new Product { Id = products[i].Id }, Amount = a })],
        };
        Assert.True(await fulano.Purchases.Insert(purchase, cancellationToken: Ct));
        return (purchase, await ItemsOf(purchase));
    }

    private async Task<IReadOnlyList<PurchaseItem>> ItemsOf(Purchase purchase) =>
        await Store.PurchaseItems.Fetch(new PurchaseItemCriteria()
            .PurchaseId.Eq(purchase.Id)
            .WithOrderBy(PurchaseItemOrderBy.OldestFirst)
            .WithProjection(new PurchaseItem { Id = Mark.Int64, Amount = Mark.Int32, Price = Mark.Decimal, Product = new Product { Id = Mark.Int64 } }),
            cancellationToken: Ct);

    private static PurchaseAmendment Errata(Purchase purchase, string reason, params (PurchaseItem Item, int After)[] lines) => new()
    {
        Purchase = new Purchase { Id = purchase.Id },
        Reason = reason,
        Lines = [.. lines.Select(l => new PurchaseAmendmentLine { ItemId = l.Item.Id, AmountAfter = l.After })],
    };

    private async Task<IReadOnlyList<PurchaseAmendment>> ErrataOf(Purchase purchase) =>
        await Store.Amendments.Fetch(new PurchaseAmendmentCriteria()
            .PurchaseId.Eq(purchase.Id)
            .WithOrderBy(PurchaseAmendmentOrderBy.OldestFirst)
            .WithProjection(new PurchaseAmendment
            {
                Id = Mark.Int64,
                Date = Mark.Instant,
                Reason = Mark.String,
                Author = new User { Id = Mark.Int64 },
                Lines = Mark.List(new PurchaseAmendmentLine
                {
                    ItemId = Mark.Int64,
                    Price = Mark.Decimal,
                    AmountBefore = Mark.Int32,
                    AmountAfter = Mark.Int32,
                    Product = new Product { Id = Mark.Int64 },
                }, new PurchaseAmendmentLineCriteria().WithOrderBy(PurchaseAmendmentLineOrderBy.OldestFirst)),
            }), cancellationToken: Ct);

    // :: The correction

    [Theory]
    [InlineData("admin")]
    [InlineData("gerente")]
    public async Task AdminAndManager_ReduceAndRemoveItems_AndTheRecordSaysWhoWhenWhyAndWhat(string corrector)
    {
        var (purchase, items) = await FulanoBuys(3, 2, 1);
        var (repositories, author) = await As(corrector);
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        Assert.True(await repositories.Amendments.Insert(
            Errata(purchase, "  Item delivered short  ", (items[0], 1), (items[1], 0)), cancellationToken: Ct));

        var now = await ItemsOf(purchase);
        Assert.Equal([(items[0].Id, 1), (items[2].Id, 1)], now.Select(i => (i.Id, i.Amount ?? 0)));
        var errata = Assert.Single(await ErrataOf(purchase));
        Assert.Equal("Item delivered short", errata.Reason);
        Assert.Equal(author, errata.Author!.Id);
        Assert.InRange(errata.Date!.Value, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Equal(
            [(items[0].Id, items[0].Product!.Id, items[0].Price, 3, 1), (items[1].Id, items[1].Product!.Id, items[1].Price, 2, 0)],
            errata.Lines!.Select(l => (l.ItemId, l.Product!.Id, l.Price, l.AmountBefore ?? -1, l.AmountAfter ?? -1)));
    }

    [Fact]
    public async Task WhatTheItemWas_ComesFromTheItem_AndWhoAndWhen_FromTheServer()
    {
        var (purchase, items) = await FulanoBuys(3, 2);
        var (admin, me) = await As("admin");
        var errata = Errata(purchase, "Correction", (items[0], 2));
        errata.Author = new User { Id = items.Count + 9999 };
        errata.Date = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        errata.Lines![0].Price = 0.01m;
        errata.Lines[0].Product = new Product { Id = items[1].Product!.Id };

        Assert.True(await admin.Amendments.Insert(errata, cancellationToken: Ct));

        var written = Assert.Single(await ErrataOf(purchase));
        Assert.Equal(me, written.Author!.Id);
        Assert.True(written.Date > DateTimeOffset.UtcNow.AddMinutes(-5));
        var line = Assert.Single(written.Lines!);
        Assert.Equal((items[0].Price, items[0].Product!.Id, 3), (line.Price, line.Product!.Id, line.AmountBefore ?? -1));
    }

    [Fact]
    public async Task TheItems_NeverChangeWithoutTheRecordOfWhatChanged_WhateverProjectionCame()
    {
        var (purchase, items) = await FulanoBuys(3);
        var (admin, _) = await As("admin");

        // In process the errata is written whole; through HTTP only what the projection marks travels (no lines),
        // and an errata without lines is refused.
        bool inserted;
        try
        {
            inserted = await admin.Amendments.Insert(Errata(purchase, "Correction", (items[0], 1)),
                new PurchaseAmendment { Purchase = new Purchase(), Reason = Mark.String }, Ct);
        }
        catch (InvalidRequestException)
        {
            inserted = false;
        }

        if (inserted)
        {
            Assert.Single(Assert.Single(await ErrataOf(purchase)).Lines!);
        }
        else
        {
            Assert.Equal(3, (await ItemsOf(purchase))[0].Amount);
            Assert.Empty(await ErrataOf(purchase));
        }
    }

    [Fact]
    public async Task TwoErrataOfTheSamePurchase_EachStartsFromWhatTheOtherLeft()
    {
        var (purchase, items) = await FulanoBuys(5);
        var (admin, _) = await As("admin");
        var (gerente, _) = await As("gerente");

        // Whichever goes second starts from what the first left: it is applied (4 after 3 is refused as going up).
        await Task.WhenAll(
            Attempt(() => admin.Amendments.Insert(Errata(purchase, "First", (items[0], 4)), cancellationToken: Ct)),
            Attempt(() => gerente.Amendments.Insert(Errata(purchase, "Second", (items[0], 3)), cancellationToken: Ct)));

        var lines = (await ErrataOf(purchase)).Select(e => Assert.Single(e.Lines!)).ToList();
        Assert.NotEmpty(lines);
        Assert.Equal(5, lines[0].AmountBefore);
        for (var i = 1; i < lines.Count; i++)
        {
            Assert.Equal(lines[i - 1].AmountAfter, lines[i].AmountBefore); // a chain, never two corrections of the same 5
        }

        Assert.Equal(lines[^1].AmountAfter, (await ItemsOf(purchase))[0].Amount);
        Assert.Equal(3, lines[^1].AmountAfter);

        static async Task Attempt(Func<Task<bool>> insert)
        {
            try
            {
                await insert();
            }
            catch (InvalidRequestException)
            {
                // refused: the other one already went below
            }
        }
    }

    // :: What a correction is not

    [Theory]
    [InlineData(3)] // the same
    [InlineData(4)] // more
    [InlineData(-1)]
    public async Task AnErrata_OnlyGoesDown(int after)
    {
        var (purchase, items) = await FulanoBuys(3, 1);
        var (admin, _) = await As("admin");

        await Assert.ThrowsAsync<InvalidRequestException>(() =>
            admin.Amendments.Insert(Errata(purchase, "Correction", (items[1], 0), (items[0], after)), cancellationToken: Ct));

        Assert.Equal([3, 1], (await ItemsOf(purchase)).Select(i => i.Amount ?? 0));
        Assert.Empty(await ErrataOf(purchase));
    }

    [Fact]
    public async Task AnErrata_DoesNotRemoveEveryItem()
    {
        var (purchase, items) = await FulanoBuys(3, 1);
        var (admin, _) = await As("admin");

        await Assert.ThrowsAsync<InvalidRequestException>(() =>
            admin.Amendments.Insert(Errata(purchase, "Cancel", (items[0], 0), (items[1], 0)), cancellationToken: Ct));

        Assert.Equal(2, (await ItemsOf(purchase)).Count);
    }

    [Fact]
    public async Task AnErrata_NeedsAReason_ALine_ItemsOfItsPurchase_EachOnce_AndTheAmountItHad()
    {
        var (purchase, items) = await FulanoBuys(3, 2);
        var (other, otherItems) = await FulanoBuys(1);
        var (admin, _) = await As("admin");
        var stale = Errata(purchase, "Correction", (items[0], 1));
        stale.Lines![0].AmountBefore = 4;
        PurchaseAmendment[] refused =
        [
            Errata(purchase, "   ", (items[0], 1)),
            Errata(purchase, "Correction"),
            Errata(purchase, "Correction", (otherItems[0], 0)),
            Errata(purchase, "Correction", (items[0], 2), (items[0], 1)),
            stale,
            new() { Reason = "Correction", Lines = [new PurchaseAmendmentLine { ItemId = items[0].Id, AmountAfter = 1 }] },
        ];

        foreach (var errata in refused)
        {
            await Assert.ThrowsAsync<InvalidRequestException>(() => admin.Amendments.Insert(errata, cancellationToken: Ct));
        }

        Assert.Equal([3, 2], (await ItemsOf(purchase)).Select(i => i.Amount ?? 0));
        Assert.Equal(1, (await ItemsOf(other))[0].Amount);
        Assert.Empty(await ErrataOf(purchase));
    }

    [Theory]
    [InlineData("fulano")]
    [InlineData("beotrano")]
    public async Task ACustomer_DoesNotCorrect(string user)
    {
        var (purchase, items) = await FulanoBuys(3);
        var (customer, _) = await As(user);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            customer.Amendments.Insert(Errata(purchase, "I want fewer", (items[0], 1)), cancellationToken: Ct));

        Assert.Equal(3, (await ItemsOf(purchase))[0].Amount);
    }

    [Fact]
    public async Task AnErrata_DoesNotEnterWithItsPurchase()
    {
        var (admin, _) = await As("admin");
        var product = (await Store.Products.Fetch(new ProductCriteria(), limit: 1, cancellationToken: Ct))[0];

        await Assert.ThrowsAsync<AccessDeniedException>(() => admin.Purchases.Insert(new Purchase
        {
            BuyDate = DateTimeOffset.UtcNow,
            Items = [new PurchaseItem { Product = new Product { Id = product.Id }, Amount = 2 }],
            Amendments = [new PurchaseAmendment { Reason = "Already corrected", Lines = [] }],
        }, cancellationToken: Ct));
    }

    [Fact]
    public async Task AnErrata_DoesNotChangeNorLeave_ExceptByMaintenance()
    {
        var (purchase, items) = await FulanoBuys(3, 2);
        var (admin, _) = await As("admin");
        Assert.True(await admin.Amendments.Insert(Errata(purchase, "Correction", (items[0], 1)), cancellationToken: Ct));
        var errata = Assert.Single(await ErrataOf(purchase));
        var byId = new PurchaseAmendmentCriteria().PurchaseAmendmentId.Eq(errata.Id);
        var lineById = new PurchaseAmendmentLineCriteria().PurchaseAmendmentId.Eq(errata.Id);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            admin.Amendments.Update(new PurchaseAmendment { Id = errata.Id, Reason = "Another reason" }, cancellationToken: Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => admin.Amendments.Delete(byId, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => admin.AmendmentLines.Delete(lineById, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => admin.AmendmentLines.Insert(new PurchaseAmendmentLine
        {
            Amendment = new PurchaseAmendment { Id = errata.Id }, ItemId = items[1].Id, Price = 1m, AmountBefore = 2, AmountAfter = 1,
            Product = new Product { Id = items[1].Product!.Id },
        }, cancellationToken: Ct));
        Assert.Equal("Correction", Assert.Single(await ErrataOf(purchase)).Reason);

        var (suporte, _) = await As("suporte");
        Assert.Equal(1, await suporte.Amendments.Delete(byId, Ct));
        Assert.Empty(await ErrataOf(purchase));
    }

    [Fact]
    public async Task Maintenance_DeletesAPurchase_WithItsErrata()
    {
        var (purchase, items) = await FulanoBuys(3, 2);
        var (admin, _) = await As("admin");
        Assert.True(await admin.Amendments.Insert(Errata(purchase, "Correction", (items[0], 1)), cancellationToken: Ct));
        var (suporte, _) = await As("suporte");

        Assert.Equal(1, await suporte.Purchases.Delete(new PurchaseCriteria().PurchaseId.Eq(purchase.Id), Ct));

        Assert.Empty(await ErrataOf(purchase));
        Assert.Equal(0, await Store.AmendmentLines.Count(new PurchaseAmendmentLineCriteria().ItemId.Eq(items[0].Id), Ct));
    }

    // :: Who sees it

    [Fact]
    public async Task TheBuyer_SeesTheErrataOfTheirPurchase_ButNotWhoMadeIt_AndNobodyElseSeesIt()
    {
        var (purchase, items) = await FulanoBuys(3, 2);
        var (admin, _) = await As("admin");
        Assert.True(await admin.Amendments.Insert(Errata(purchase, "Correction", (items[1], 0)), cancellationToken: Ct));
        var (fulano, _) = await As("fulano");
        var (beotrano, _) = await As("beotrano");
        var withErrata = new Purchase
        {
            Id = Mark.Int64,
            Amendments = Mark.List(new PurchaseAmendment
            {
                Reason = Mark.String,
                Author = new User { Id = Mark.Int64, Name = Mark.String },
                Lines = Mark.List(new PurchaseAmendmentLine { AmountAfter = Mark.Int32 }),
            }),
        };
        var direct = new PurchaseAmendmentCriteria().PurchaseId.Eq(purchase.Id)
            .WithProjection(new PurchaseAmendment { Reason = Mark.String, Author = new User { Id = Mark.Int64, Name = Mark.String } });

        var seen = Assert.Single(await fulano.Purchases.Fetch(
            new PurchaseCriteria().PurchaseId.Eq(purchase.Id).WithProjection(withErrata), cancellationToken: Ct));
        var errata = Assert.Single(seen.Amendments!);
        Assert.Equal("Correction", errata.Reason);
        Assert.Null(errata.Author);
        Assert.Equal(0, Assert.Single(errata.Lines!).AmountAfter);
        Assert.Null(Assert.Single(await fulano.Amendments.Fetch(direct, cancellationToken: Ct)).Author);

        Assert.Empty(await beotrano.Amendments.Fetch(new PurchaseAmendmentCriteria().PurchaseId.Eq(purchase.Id), cancellationToken: Ct));
        Assert.Equal(0, await beotrano.AmendmentLines.Count(new PurchaseAmendmentLineCriteria().ItemId.Eq(items[1].Id), Ct));

        var staff = Assert.Single(await admin.Amendments.Fetch(new PurchaseAmendmentCriteria().PurchaseId.Eq(purchase.Id)
            .WithProjection(new PurchaseAmendment { Author = new User { Name = Mark.String } }), cancellationToken: Ct));
        Assert.Equal("John Smith", staff.Author!.Name);
    }

    [Fact]
    public async Task TheGenericItemWrites_StayRefused_AfterAnErrata()
    {
        var (purchase, items) = await FulanoBuys(3, 2);
        var (admin, _) = await As("admin");

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            admin.PurchaseItems.Update(new PurchaseItem { Id = items[0].Id, Amount = 1 },
                projection: new PurchaseItem { Amount = Mark.Int32 }, cancellationToken: Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            admin.PurchaseItems.Delete(new PurchaseItemCriteria().PurchaseItemId.Eq(items[1].Id), Ct));
        Assert.Equal([3, 2], (await ItemsOf(purchase)).Select(i => i.Amount ?? 0));
    }
}
