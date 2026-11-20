using Wdc.Cube;

namespace Contacts.List;

// <snippet:list-scope>
public sealed record ContactRow(long Id, string Name);

public sealed class ContactListScope : Scope
{
    public IReadOnlyList<ContactRow> Contacts { get; set => Set(ref field, value); } = [];

    /// <summary>The open contact (the place below the list), or null.</summary>
    public Scope? Detail { get; set => Set(ref field, value); }

    public Func<long, Task> OnOpen { get; set; } = ScopeActions.Unbound<long>();
}
// </snippet:list-scope>
