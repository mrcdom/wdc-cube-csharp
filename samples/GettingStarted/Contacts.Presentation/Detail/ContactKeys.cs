using Wdc.Cube;

namespace Contacts.Detail;

// <snippet:contact-keys>
public sealed class ContactKeys : AppKeys
{
    public new const string Prefix = "ct";

    private const string ContactIdName = $"{Prefix}-id";

    public ContactKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Places.Contact)
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
