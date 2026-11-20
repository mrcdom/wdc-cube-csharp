using Contacts.Detail;
using Contacts.List;
using Wdc.Cube;

namespace Contacts;

// <snippet:places>
/// <summary>The cube: every place an address can name. <c>contacts/contact?ct-id=2</c> is the detail of contact 2.</summary>
public static class Places
{
    public static readonly Place Contacts = Place.Create<ContactsApp>("contacts", app => new ContactListPresenter(app));

    public static readonly Place Contact = Contacts.Child<ContactsApp>("contact", app => new ContactDetailPresenter(app));

    public static IReadOnlyList<Place> All { get; } = [Contacts, Contact];
}
// </snippet:places>
