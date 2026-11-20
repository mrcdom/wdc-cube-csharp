# Dispatcher

Every Cube application has one `PresentationDispatcher`: a serial queue where all of its presentation code runs, one
item at a time. Actions, navigation, scope flushes and history updates all run there, and every `await` inside them
comes back there. The result is that presenters are written as if the application had a single thread — no locks, no
races between a click and a load that finishes — while the host decides which real thread that is (Blazor's renderer,
a desktop UI thread, or a queue of its own in tests and servers).

## The rules

- **One presentation thread per application.** Items never overlap. They interleave only at `await` points, like an
  event loop.
- **Awaits come back.** While an item runs, the dispatcher's `SynchronizationContext` is installed, so the
  continuation of an `await` is queued on the same dispatcher.
- **Entry points marshal themselves.** Members that return a `Task` (`Flip`, `FlipToIntent`, `FlipToIntentString`,
  `KickStart`, `InvokeAsync`, and every action built with `Action(...)` or `Mirror(...)`) can be called from any
  thread: they run on the dispatcher.
- **Synchronous members verify.** Members that read or change the navigation or scope state synchronously
  (`NewFlipIntent`, `NewIntentFromString`, `UpdateHistory`, `Release`, `Updates.FlushNow`, changing an owned scope)
  call `VerifyAccess()` and throw `InvalidOperationException` when called from outside.
- **Errors are not swallowed.** An exception from a posted item that nobody awaited is raised on
  `UnhandledException`; the application forwards it to `CubeApplication.Unexpected`.

## The API

```csharp
public abstract class PresentationDispatcher
{
    public abstract bool CheckAccess();                 // am I on it?
    public void VerifyAccess();                         // throw if not
    public void Post(Action action);                    // queue, fire and forget
    public Task InvokeAsync(Action action);             // run there; inline when already there
    public Task<T> InvokeAsync<T>(Func<T> func);
    public Task InvokeAsync(Func<Task> work);           // its awaits come back there
    public Task<T> InvokeAsync<T>(Func<Task<T>> work);
    public event Action<Exception>? UnhandledException;
    public static PresentationDispatcher FromHost(Action<Action> post, Func<bool> checkAccess);
}
```

- `InvokeAsync` runs **inline** when the caller is already on the dispatcher, and queues otherwise. Exceptions reach
  the caller through the returned task.
- `InvokeAsync` flows the caller's `ExecutionContext` (its `AsyncLocal` values), as an `await` would. `Post` does not,
  like any `SynchronizationContext.Post`.
- `CubeApplication` exposes the same `InvokeAsync` overloads and the `Dispatcher` itself.

## No `ConfigureAwait(false)` in presentation code

`ConfigureAwait(false)` tells an `await` not to come back to the captured context. In a presenter that means the rest
of the method runs on a thread-pool thread, outside the dispatcher: changing a scope there throws, and anything that
does not verify (a field, a list) is now touched from two threads.

```csharp
await Task.Delay(1).ConfigureAwait(false);
App.Dispatcher.CheckAccess(); // false: we left
Scope.Loading = false;        // InvalidOperationException
```

Library code below the presentation (repositories, HTTP clients) may use `ConfigureAwait(false)` internally: the
presenter's own `await` on it still comes back to the dispatcher.

## Doing work elsewhere

The dispatcher serializes; it does not make work faster. Long CPU work or blocking I/O runs elsewhere and the result
comes back through an `await`:

```csharp
private async Task OnExport()
{
    Scope.Busy = true;
    var file = await Task.Run(() => BuildReport(_rows)); // on the pool
    Scope.Busy = false;                                  // back on the dispatcher
    Scope.FileName = file;
}
```

Callbacks that do not come from an `await` (a timer, an event raised on another thread) hop back with `Post` or
`InvokeAsync`, or check first:

```csharp
public void ShowError(string message)
{
    if (!Dispatcher.CheckAccess())
    {
        Dispatcher.Post(() => ShowError(message));
        return;
    }

    Scope.ErrorMessage = message;
    _errorTimer?.Dispose();
    _errorTimer = Time.CreateTimer(_ => Dispatcher.Post(() =>
    {
        if (Scope.ErrorMessage == message)
        {
            Scope.ErrorMessage = null;
        }
    }), null, ErrorToastDuration, Timeout.InfiniteTimeSpan);
}
```

Use the application's `Time` (`TimeProvider`) for timers, so tests can control time.

## Hosts

The dispatcher is passed to the `CubeApplication` constructor. When none is given, the application creates a
`SerialDispatcher` and disposes it on `Release()`; a dispatcher that was given is never disposed by the application.

| Host | Dispatcher | Notes |
|---|---|---|
| Blazor (Server and WebAssembly) | `BlazorPresentation.CreateDispatcher(renderer.Dispatcher)` | `CubeHost<TApp>` does it for you and passes it in `CubeHostContext.Dispatcher`. One application per circuit (Server) or per page (WebAssembly). |
| Avalonia | `AvaloniaPresentation.CreateDispatcher()` | Avalonia's UI thread: presenters, flushes and views share it. |
| Servers, background hosts, most tests | `SerialDispatcher` (the default) | Its own queue, drained by one consumer on the thread pool. Consecutive items may run on different pool threads, never at the same time. |
| Tests that control interleavings | `ManualDispatcher` (`Wdc.Cube.Testing`) | Nothing runs until the test pumps it. |

`PresentationDispatcher.FromHost(post, checkAccess)` adapts any other host that already has a serial context of its
own (a WPF or WinForms UI thread): the host's synchronization context brings the awaits back, so the adapter only needs
to post and to answer whether the caller is on it.

```csharp
// Blazor: what CubeHost does
var dispatcher = BlazorPresentation.CreateDispatcher(renderHandle.Dispatcher);

// Avalonia: the desktop sample's composition
var history = new MemoryHistoryManager(address);
var presenter = new MainPresenter(history, client.Repositories, new MemorySessionStore(),
    dispatcher: AvaloniaPresentation.CreateDispatcher(), authentication: client.Authentication);
```

## Testing with dispatchers

With the default `SerialDispatcher`, a test calls the async entry points and awaits them; `await app.Settle()` lets
queued work and pending flushes finish before asserting.

`ManualDispatcher` runs everything on the test's thread, in exactly the order it was queued, only when asked:

- `RunOne()` runs one item; `RunPending()` runs until the queue is empty (with a ceiling that turns an endless loop
  into a failure).
- `Run(work)` runs `work` on the dispatcher and pumps until it completes, waiting for real asynchronous continuations
  (with a timeout instead of a hang).
- `Unhandled` collects exceptions nobody awaited.

With the [getting-started](../getting-started.md) app:

<!-- snippet: manual-dispatcher -->
```csharp
var dispatcher = new ManualDispatcher();
var app = new ContactsApp(new TestHistoryManager(), new ContactBook(), dispatcher);

var start = app.Start();                // queued on the dispatcher: nothing ran yet
Assert.False(start.IsCompleted);
Assert.Null(app.Scope.Body);

dispatcher.Run(() => start);            // runs it on the test's thread, to the end
Assert.IsType<ContactListScope>(app.Scope.Body);
```

## See also

- [Architecture](architecture.md)
- [Update cycle](update-cycle.md)
- [Navigation](navigation.md)
- [History](history.md)
- [Domain](domain.md), [Persistence](persistence.md), [Transactions](transactions.md),
  [Protected repositories](protected-repositories.md), [Remote](remote.md)
- [Getting started](../getting-started.md)
