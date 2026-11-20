using Contacts.List;
using Wdc.Cube.Testing;

namespace Contacts.Tests;

/// <summary>The application on a dispatcher the test pumps by hand: nothing runs until the test says so.</summary>
public sealed class ManualDispatcherTests
{
    [Fact]
    public void TheWorkWaitsInTheQueue_UntilTheTestRunsIt()
    {
        // <snippet:manual-dispatcher>
        var dispatcher = new ManualDispatcher();
        var app = new ContactsApp(new TestHistoryManager(), new ContactBook(), dispatcher);

        var start = app.Start();                // queued on the dispatcher: nothing ran yet
        Assert.False(start.IsCompleted);
        Assert.Null(app.Scope.Body);

        dispatcher.Run(() => start);            // runs it on the test's thread, to the end
        Assert.IsType<ContactListScope>(app.Scope.Body);
        // </snippet:manual-dispatcher>
    }
}
