namespace Contacts;

/// <summary>Where the contacts come from. The presenters only know this contract.</summary>
public interface IContactBook
{
    Task<IReadOnlyList<Contact>> All();

    Task<Contact?> Find(long id);
}
