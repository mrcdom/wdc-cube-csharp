using Contacts.Detail;
using Contacts.List;
using Wdc.Cube.Testing;

namespace Contacts.Tests;

// <snippet:tests>
/// <summary>The whole application, driven as a view would drive it, with no view at all.</summary>
public sealed class ContactsTests
{
    private static async Task<(ContactsApp App, TestHistoryManager History)> Start(string address = "")
    {
        var history = new TestHistoryManager(address);
        var app = new ContactsApp(history, new ContactBook());
        await app.Start();
        await app.Settle();
        return (app, history);
    }

    private static ContactListScope List(ContactsApp app) => Assert.IsType<ContactListScope>(app.Scope.Body);

    [Fact]
    public async Task AnEmptyAddress_ShowsTheList()
    {
        var (app, history) = await Start();

        Assert.Equal(["Ada Lovelace", "Alan Turing", "Grace Hopper"], List(app).Contacts.Select(c => c.Name));
        Assert.Null(List(app).Detail);
        Assert.Equal("contacts", history.Token);
    }

    [Fact]
    public async Task OpeningAContact_ShowsItBesideTheList_AndNamesItInTheAddress()
    {
        var (app, history) = await Start();

        await List(app).OnOpen(2);
        await app.Settle();

        var detail = Assert.IsType<ContactDetailScope>(List(app).Detail);
        Assert.Equal("Alan Turing", detail.Name);
        Assert.Equal("contacts/contact?ct-id=2", history.Token);
    }

    [Fact]
    public async Task TheAddressAlone_RebuildsTheScreen()
    {
        var (app, _) = await Start("contacts/contact?ct-id=3");

        Assert.Equal("Grace Hopper", Assert.IsType<ContactDetailScope>(List(app).Detail).Name);
    }

    [Fact]
    public async Task AnUnknownContact_FallsBackToTheList()
    {
        var (app, history) = await Start("contacts/contact?ct-id=99");

        Assert.Null(List(app).Detail);
        Assert.Equal("contacts", history.Token);
    }

    [Fact]
    public async Task Closing_GoesBackToTheList()
    {
        var (app, history) = await Start("contacts/contact?ct-id=1");

        await Assert.IsType<ContactDetailScope>(List(app).Detail).OnClose();
        await app.Settle();

        Assert.Null(List(app).Detail);
        Assert.Equal("contacts", history.Token);
    }
}
// </snippet:tests>
