namespace Contacts;

/// <summary>A few contacts in memory.</summary>
public sealed class ContactBook : IContactBook
{
    private static readonly Contact[] s_contacts =
    [
        new(1, "Ada Lovelace", "ada@example.com", "+44 20 0000 0001"),
        new(2, "Alan Turing", "alan@example.com", "+44 20 0000 0002"),
        new(3, "Grace Hopper", "grace@example.com", "+1 212 000 0003"),
    ];

    public Task<IReadOnlyList<Contact>> All() => Task.FromResult<IReadOnlyList<Contact>>(s_contacts);

    public Task<Contact?> Find(long id) => Task.FromResult(s_contacts.FirstOrDefault(c => c.Id == id));
}
