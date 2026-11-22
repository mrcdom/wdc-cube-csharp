using Contacts.List;
using Wdc.Cube;

namespace Contacts.Detail;

// <snippet:contact-keys>
public sealed class ContactKeys : ContactListKeys
{
    public new const string Prefix = "ct";

    /// <summary>A child of the list: <c>contacts/contact?ct-id=2</c> is the detail of contact 2.</summary>
    public static new Place Place { get; } =
        ContactListKeys.Place.Child<ContactsApp>("contact", app => new ContactDetailPresenter(app));

    private const string ContactIdName = $"{Prefix}-id";

    public ContactKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }

    /// <summary>A parameter: it goes to the address. Garbage reads as none.</summary>
    public long? ContactId
    {
        get => Intent.GetInt64(ContactIdName) is > 0 and var id ? id : null;
        set => Intent.SetParameter(ContactIdName, value);
    }
}
// </snippet:contact-keys>
