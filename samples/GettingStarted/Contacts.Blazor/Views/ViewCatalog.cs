using Contacts.Detail;
using Contacts.List;
using Wdc.Cube.Blazor;

namespace Contacts.Blazor.Views;

// <snippet:view-catalog>
/// <summary>Which component draws each scope that can land in a slot.</summary>
public static class ViewCatalog
{
    public static void Register(ViewRegistry views) => views
        .Register<RootScope, RootView>()
        .Register<ContactListScope, ContactListView>()
        .Register<ContactDetailScope, ContactDetailView>();
}
// </snippet:view-catalog>
