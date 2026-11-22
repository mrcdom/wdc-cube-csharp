using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Admin;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Shopping.Presentation.Modules.Home.Receipt;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>
/// The administration: the purchases of every customer and their correction, offered to whoever may
/// correct them; the buyer sees the correction on the receipt.
/// </summary>
public abstract class AdminTestsBase(ShoppingDatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A purchase made by fulano (through his own protected repositories), one item per amount.</summary>
    private async Task<long> FulanoBuys(params int[] amounts)
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var (fulano, authentication) = db.Connect();
        Assert.NotNull(await authentication.Login("fulano", "fulano", Ct));
        var products = await db.Store!.Repositories.Products.Fetch(new ProductCriteria().WithOrderBy(ProductOrderBy.NameAToZ),
            cancellationToken: Ct);
        var purchase = new Purchase
        {
            BuyDate = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero),
            Items = [.. amounts.Select((a, i) => new PurchaseItem { Product = new Product { Id = products[i].Id }, Amount = a })],
        };
        Assert.True(await fulano.Purchases.Insert(purchase, cancellationToken: Ct));
        return purchase.Id!.Value;
    }

    private static async Task<AmendPresenter> Open(Shop shop, long purchaseId)
    {
        await shop.Do(() => shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.OnOpenAdmin());
        await shop.Do(() => shop.PresenterOf<AdminPresenter>(AdminKeys.Place).Scope.OnOpenPurchase(purchaseId));
        return shop.PresenterOf<AmendPresenter>(AmendKeys.Place);
    }

    // :: Who is offered it

    [Theory]
    [InlineData("admin", true)]
    [InlineData("gerente", true)]
    [InlineData("fulano", false)]
    public async Task TheAdministration_IsOfferedToWhoeverMayCorrect(string user, bool offered)
    {
        var shop = await Shop.LoggedIn(db, user);

        Assert.Equal(offered, shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.ShowAdmin);
    }

    [Fact]
    public async Task ACustomer_WhoTypesItsAddress_GoesHome()
    {
        var shop = await Shop.LoggedIn(db, "fulano");

        var typed = await Shop.Start(db, "home/admin", shop.Session);

        Assert.Equal("home", typed.History.Token);
        Assert.Equal(AdminPresenter.NotAllowed, typed.App.Scope.ErrorMessage);
        Assert.Null(typed.Maybe<AdminPresenter>(AdminKeys.Place));
    }

    // :: The list

    [Fact]
    public async Task TheList_HasEveryCustomer_NewestFirst_AndFiltersByName_InTheAddress()
    {
        var purchaseId = await FulanoBuys(1);
        var shop = await Shop.LoggedIn(db, "admin");
        await shop.Do(() => shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.OnOpenAdmin());
        var admin = shop.PresenterOf<AdminPresenter>(AdminKeys.Place).Scope;

        Assert.Contains(admin.Purchases, p => p.CustomerLabel == "John Doe");
        Assert.Contains(admin.Purchases, p => p.CustomerLabel == "John Smith");

        await shop.Do(() => admin.OnFilter("doe"));

        Assert.All(admin.Purchases, p => Assert.Equal("John Doe", p.CustomerLabel));
        Assert.Contains(admin.Purchases, p => p.Id == purchaseId);
        Assert.Equal("doe", admin.CustomerFilter);
        Assert.Equal("home/admin?ad-customer=doe", shop.History.Token);
        var again = await Shop.Start(db, shop.History.Token, shop.Session);
        Assert.All(again.PresenterOf<AdminPresenter>(AdminKeys.Place).Scope.Purchases, p => Assert.Equal("John Doe", p.CustomerLabel));
    }

    // :: The correction

    [Fact]
    public async Task Correcting_ChangesThePurchase_KeepsItsHistory_AndTheBuyerSeesIt()
    {
        var purchaseId = await FulanoBuys(3, 2);
        var shop = await Shop.LoggedIn(db, "gerente");
        var amend = await Open(shop, purchaseId);
        var form = amend.Scope;
        Assert.Equal($"Purchase #{purchaseId}", form.TitleLabel);
        Assert.Equal("John Doe", form.CustomerLabel);
        Assert.Equal([3, 2], form.Lines.Select(l => l.Quantity));
        Assert.False(form.CanConfirm);

        await shop.Do(() => form.Lines[0].OnChangeNewQuantity(1));
        await shop.Do(() => form.Lines[1].OnChangeNewQuantity(0));
        await shop.Do(() => form.OnChangeReason("Short delivery"));
        Assert.True(form.CanConfirm);
        Assert.Equal("$199.99", form.NewTotalLabel); // one Italian Design Coffee Maker
        await shop.Do(form.OnConfirm);

        Assert.Equal(AmendPresenter.Saved, form.SuccessMessage);
        Assert.Equal([1], form.Lines.Select(l => l.Quantity));
        var entry = Assert.Single(form.History);
        Assert.Equal(("Store Manager", "Short delivery"), (entry.AuthorLabel, entry.Reason));
        Assert.Equal(["Italian Design Coffee Maker: 3 → 1", "Thread Seal Tape: 2 → 0"], entry.Changes);
        Assert.Equal(string.Empty, form.Reason);
        Assert.Contains(shop.PresenterOf<AdminPresenter>(AdminKeys.Place).Scope.Purchases, p => p.Id == purchaseId && p.Corrected);

        var buyer = await Shop.LoggedIn(db, "fulano");
        await buyer.Do(() => buyer.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Purchases!.OnOpenReceipt(purchaseId));
        var receipt = buyer.PresenterOf<ReceiptPresenter>(ReceiptKeys.Place).Scope;
        Assert.Equal("$199.99", receipt.TotalLabel);
        Assert.Equal(["Purchase corrected on " + Infra.Formats.Date(DateTimeOffset.UtcNow) + ": Short delivery"], receipt.Corrections);
    }

    [Fact]
    public async Task TheForm_SaysWhatIsMissing_BeforeAnythingIsSent()
    {
        var purchaseId = await FulanoBuys(3, 2);
        var shop = await Shop.LoggedIn(db, "admin");
        var form = (await Open(shop, purchaseId)).Scope;

        await shop.Do(form.OnConfirm);
        Assert.Equal(AmendPresenter.NothingToCorrect, form.ErrorMessage);

        await shop.Do(() => form.Lines[0].OnChangeNewQuantity(4));
        Assert.Equal(AmendPresenter.OnlyDown, form.ErrorMessage);
        Assert.Equal(3, form.Lines[0].NewQuantity);

        await shop.Do(() => form.Lines[0].OnChangeNewQuantity(0));
        await shop.Do(() => form.Lines[1].OnChangeNewQuantity(0));
        await shop.Do(() => form.OnChangeReason("Cancel"));
        await shop.Do(form.OnConfirm);
        Assert.Equal(AmendPresenter.EveryItemRemoved, form.ErrorMessage);

        await shop.Do(() => form.Lines[1].OnChangeNewQuantity(2));
        await shop.Do(() => form.OnChangeReason("  "));
        await shop.Do(form.OnConfirm);
        Assert.Equal(AmendPresenter.ReasonRequired, form.ErrorMessage);

        Assert.Equal(0, await db.Store!.Repositories.Amendments.Count(new PurchaseAmendmentCriteria().PurchaseId.Eq(purchaseId), Ct));
    }

    [Fact]
    public async Task ACorrectionMadeMeanwhile_IsNotOverwritten_TheFormComesBackAsItIsNow()
    {
        var purchaseId = await FulanoBuys(3);
        var shop = await Shop.LoggedIn(db, "admin");
        var form = (await Open(shop, purchaseId)).Scope;
        var (gerente, authentication) = db.Connect();
        Assert.NotNull(await authentication.Login("gerente", "gerente", Ct));
        var item = Assert.Single(await db.Store!.Repositories.PurchaseItems.Fetch(
            new PurchaseItemCriteria().PurchaseId.Eq(purchaseId), cancellationToken: Ct));
        Assert.True(await gerente.Amendments.Insert(new PurchaseAmendment
        {
            Purchase = new Purchase { Id = purchaseId },
            Reason = "Before",
            Lines = [new PurchaseAmendmentLine { ItemId = item.Id, AmountAfter = 2 }],
        }, cancellationToken: Ct));

        await shop.Do(() => form.Lines[0].OnChangeNewQuantity(1));
        await shop.Do(() => form.OnChangeReason("After"));
        await shop.Do(form.OnConfirm);

        Assert.Equal(AmendPresenter.ChangedMeanwhile, form.ErrorMessage);
        Assert.Equal(2, form.Lines[0].Quantity);
        Assert.Equal("Before", Assert.Single(form.History).Reason);
    }

    [Fact]
    public async Task BackFromTheForm_ReturnsToTheListAsItWas()
    {
        var purchaseId = await FulanoBuys(2);
        var shop = await Shop.LoggedIn(db, "admin");
        await shop.Do(() => shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.OnOpenAdmin());
        var admin = shop.PresenterOf<AdminPresenter>(AdminKeys.Place);
        await shop.Do(() => admin.Scope.OnFilter("Doe"));
        await shop.Do(() => admin.Scope.OnOpenPurchase(purchaseId));
        Assert.StartsWith("home/admin/amend?", shop.History.Token, StringComparison.Ordinal);
        Assert.Same(shop.PresenterOf<AmendPresenter>(AmendKeys.Place).Scope, admin.Scope.Content);

        await shop.Do(shop.PresenterOf<AmendPresenter>(AmendKeys.Place).Scope.OnBack);

        Assert.Equal("home/admin?ad-customer=Doe", shop.History.Token);
        Assert.Null(admin.Scope.Content);
        Assert.Equal("Doe", admin.Scope.CustomerFilter);
    }
}
