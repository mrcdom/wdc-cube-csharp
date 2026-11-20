using Wdc.Cube;

namespace Contacts.Detail;

// <snippet:detail-scope>
public sealed class ContactDetailScope : Scope
{
    public string Name { get; set => Set(ref field, value); } = string.Empty;

    public string Email { get; set => Set(ref field, value); } = string.Empty;

    public string Phone { get; set => Set(ref field, value); } = string.Empty;

    public Func<Task> OnClose { get; set; } = ScopeActions.Unbound();
}
// </snippet:detail-scope>
