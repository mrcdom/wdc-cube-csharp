using Contacts.Detail;
using Contacts.List;
using Microsoft.Extensions.Logging;
using Wdc.Cube;

namespace Contacts;

// <snippet:app>
/// <summary>The application presenter: the page frame, and the services the presenters below it use.</summary>
public sealed class ContactsApp : ApplicationPresenter<AppScope>
{
    /// <summary>Every place an address can name, each declared by its keys.</summary>
    public static IReadOnlyList<Place> AllPlaces { get; } = [ContactListKeys.Place, ContactKeys.Place];

    public ContactsApp(HistoryManager history, IContactBook book, PresentationDispatcher? dispatcher = null,
        ILogger? logger = null)
        : base(new AppScope(), history, AllPlaces, logger, dispatcher)
    {
        Book = book;
    }

    public IContactBook Book { get; }

    /// <summary>Starts from the address; an empty one shows the list.</summary>
    public Task Start() => InvokeAsync(async () =>
    {
        await KickStart(ContactListKeys.Place);
        if (!HasNavigated)
        {
            await new ContactListKeys(this).Flip();
        }
    });

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        if (last)
        {
            Scope.Body = null; // the application itself: nothing below it
        }
        else
        {
            new AppKeys(this, intent).ParentSlot = body => Scope.Body = body;
        }

        return Task.FromResult(true);
    }
}
// </snippet:app>
