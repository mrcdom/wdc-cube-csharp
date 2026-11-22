using Contacts.Detail;
using Wdc.Cube;

namespace Contacts.List;

// <snippet:list-presenter>
public sealed class ContactListPresenter(ContactsApp app)
    : CubePresenter<ContactsApp, ContactListScope>(app, new ContactListScope())
{
    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new ContactListKeys(App, intent);
        keys.ParentSlot(Scope); // into the application's body

        if (initialization)
        {
            Scope.OnOpen = Action<long>(id => new ContactKeys(App) { ContactId = id }.Flip());
            var contacts = await App.Book.All();
            Scope.Contacts = [.. contacts.Select(c => new ContactRow(c.Id, c.Name))];
        }

        if (last)
        {
            Scope.Detail = null; // the list itself: no contact open
        }
        else
        {
            keys.ParentSlot = detail => Scope.Detail = detail; // the contact shows beside the list
        }

        return true;
    }
}
// </snippet:list-presenter>
