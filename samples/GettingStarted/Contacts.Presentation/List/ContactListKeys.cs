using Wdc.Cube;

namespace Contacts.List;

// <snippet:list-keys>
/// <summary>The list: a child of the application. It has no names of its own; the contact's keys extend these.</summary>
public class ContactListKeys : AppKeys
{
    public new const string Prefix = "cl";

    public static new Place Place { get; } = AppKeys.Place.Child<ContactsApp>("contacts", app => new ContactListPresenter(app));

    public ContactListKeys(CubeApplication app, FlipIntent? intent = null)
        : this(app, intent, Place)
    {
    }

    protected ContactListKeys(CubeApplication app, FlipIntent? intent, Place place)
        : base(app, intent, place)
    {
    }
}
// </snippet:list-keys>
