using Shopping.Domain.Users;
using Shopping.Presentation.Modules.Login;

namespace Shopping.Presentation.Tests;

/// <summary>Guessing a password: after the free failures the login says how long to wait, even for the right one.</summary>
public abstract class LoginThrottleTestsBase(ShoppingDatabaseFixture db)
{
    [Fact]
    public async Task AfterTooManyFailures_EvenTheRightPasswordWaits_AndTheMessageSaysHowLong()
    {
        var shop = await Shop.Start(db);
        var userName = "guess" + Guid.NewGuid().ToString("N")[..8];
        await db.Store!.Repositories.Users.Insert(new User
        {
            UserName = userName,
            Password = PasswordHash.Hash("right"),
            Name = "Guessed",
            Roles = "Customer",
        }, cancellationToken: TestContext.Current.CancellationToken);
        var login = shop.PresenterOf<LoginPresenter>(LoginKeys.Place);

        for (var i = 0; i < 6; i++)
        {
            await shop.Login(userName, "wrong");
            Assert.Equal(LoginPresenter.NotRecognized, login.Scope.ErrorMessage);
        }

        await shop.Login(userName, "right");

        Assert.Matches(@"^Too many failed attempts\. Try again in (2\d|30) seconds\.$", login.Scope.ErrorMessage);
        Assert.Null(shop.App.Subject);
        Assert.False(login.Scope.Loading);
    }
}
