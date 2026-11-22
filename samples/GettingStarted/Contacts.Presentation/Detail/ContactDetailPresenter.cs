using Contacts.List;
using Wdc.Cube;

namespace Contacts.Detail;

// <snippet:detail-presenter>
public sealed class ContactDetailPresenter(ContactsApp app)
    : CubePresenter<ContactsApp, ContactDetailScope>(app, new ContactDetailScope())
{
    private long? _contactId;

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new ContactKeys(App, intent);
        var contact = keys.ContactId is { } id ? await App.Book.Find(id) : null;
        if (contact is null)
        {
            // Nothing to show: go to the list instead, within this same navigation.
            await App.FlipToIntent(intent.Redirect(ContactListKeys.Place));
            return false;
        }

        if (initialization)
        {
            Scope.OnClose = Action(() => new ContactListKeys(App).Flip());
        }

        _contactId = contact.Id;
        Scope.Name = contact.Name;
        Scope.Email = contact.Email;
        Scope.Phone = contact.Phone;
        keys.ParentSlot(Scope); // beside the list
        return true;
    }

    /// <summary>What this place puts in the address when the application publishes it.</summary>
    public override void PublishParameters(FlipIntent intent) => new ContactKeys(App, intent).ContactId = _contactId;
}
// </snippet:detail-presenter>
