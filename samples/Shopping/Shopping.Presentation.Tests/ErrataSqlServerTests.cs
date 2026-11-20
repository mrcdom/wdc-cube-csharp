namespace Shopping.Presentation.Tests;

/// <summary>
/// The errata on SQL Server: what depends on the dialect — the tables and their checks, and the locking of the
/// purchase — not the whole set again (each round trip there is slow).
/// </summary>
public sealed class ErrataSqlServerTests(ShoppingSqlServerFixture db) : IClassFixture<ShoppingSqlServerFixture>
{
    private readonly Run _run = new(db);

    [Fact]
    public Task AdminReducesAndRemovesItems() => _run.AdminAndManager_ReduceAndRemoveItems_AndTheRecordSaysWhoWhenWhyAndWhat("admin");

    [Fact]
    public Task TwoErrataOfTheSamePurchase_AreAppliedOneAfterTheOther() => _run.TwoErrataOfTheSamePurchase_EachStartsFromWhatTheOtherLeft();

    private sealed class Run(ShoppingDatabaseFixture db) : ErrataTestsBase(db);
}
