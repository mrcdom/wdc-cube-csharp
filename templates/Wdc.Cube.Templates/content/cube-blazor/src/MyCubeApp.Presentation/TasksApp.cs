using Microsoft.Extensions.Logging;
using MyCubeApp.Detail;
using MyCubeApp.Domain;
using MyCubeApp.List;
using Wdc.Cube;

namespace MyCubeApp;

/// <summary>
/// The application presenter: it holds the presenters and the navigation, owns the page frame, and hands the
/// presenters below it what they use (the tasks). One per user session: on Blazor Server, one per circuit.
/// </summary>
public sealed class TasksApp : ApplicationPresenter<RootScope>
{
    /// <summary>Every place an address can name, each declared by its keys. The root is not in it.</summary>
    public static IReadOnlyList<Place> AllPlaces { get; } = [TaskListKeys.Place, TaskKeys.Place];

    public static Message UnexpectedError { get; } = new("app.unexpectedError");

    public TasksApp(HistoryManager history, ITaskRepository tasks, PresentationDispatcher? dispatcher = null,
        ILogger? logger = null)
        : base(new RootScope(), history, AllPlaces, logger, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        Tasks = tasks;
    }

    public ITaskRepository Tasks { get; }

    /// <summary>Starts from the address; an empty one shows the list.</summary>
    public Task Start() => InvokeAsync(async () =>
    {
        await KickStart(TaskListKeys.Place);
        if (!HasNavigated)
        {
            await new TaskListKeys(this).Flip();
        }
    });

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        if (initialization)
        {
            Scope.OnDismissError = Action(OnDismissError);
        }

        if (last)
        {
            Scope.Body = null; // the application itself: nothing below it
        }
        else
        {
            new RootKeys(this, intent).ParentSlot = body => Scope.Body = body;
        }

        return Task.FromResult(true);
    }

    /// <summary>A failure no action handled: the log gets the detail, the user a message meant for them.</summary>
    public override void Unexpected(string message, Exception error)
    {
        base.Unexpected(message, error);
        if (Dispatcher.CheckAccess())
        {
            Scope.ErrorMessage = UnexpectedError;
        }
        else
        {
            Dispatcher.Post(() => Scope.ErrorMessage = UnexpectedError);
        }
    }

    private void OnDismissError() => Scope.ErrorMessage = null;
}
