using Microsoft.Extensions.Logging;
using Tutorial.Presentation.Services;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Main;

/// <summary>The application presenter: the page frame (menu, body, dialog, alert) and the services.</summary>
public sealed class MainPresenter : ApplicationPresenter<MainScope>
{
    private readonly BodyScope _bodyScope = new();
    private readonly ScopeSlot _bodySlot;
    private readonly ScopeSlot _dialogSlot;

    public MainPresenter(HistoryManager history, ITutorialService service, PresentationDispatcher? dispatcher = null,
        TimeProvider? timeProvider = null, ILogger? logger = null)
        : base(new MainScope(), history, Places.All, logger, dispatcher, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(service);
        Service = service;
        _bodySlot = scope => Scope.Body = scope ?? _bodyScope;
        _dialogSlot = scope => Scope.Dialog = scope;
    }

    public ITutorialService Service { get; }

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new MainKeys(this, intent);
        if (initialization)
        {
            Scope.OnHome = Action(OnHome);
            Scope.OnOpenTodos = Action(OnOpenTodos);
            Scope.OnOpenSubscriptions = Action(OnOpenSubscriptions);
            Scope.OnLogin = Action(OnLogin);
            _bodyScope.OnOpenAlert = Action<AlertSeverity>(OnOpenAlert);
            Scope.Body = _bodyScope;

            // No flip to the address here: on a cold start KickStart calls this with last = true even when the
            // address points deeper, and travels there itself right after.
        }

        if (last)
        {
            _bodySlot(null);
        }
        else
        {
            keys.ParentSlot = _bodySlot;
            keys.DialogSlot = _dialogSlot;
        }

        return Task.FromResult(true);
    }

    /// <summary>Errors nobody handled are logged and shown.</summary>
    public override void Unexpected(string message, Exception error)
    {
        base.Unexpected(message, error);
        Alert(AlertSeverity.Error, "Unexpected error", message);
    }

    /// <summary>Shows an alert; <paramref name="onClose"/> runs when the user dismisses it.</summary>
    public void Alert(AlertSeverity severity, string title, string message, Func<Task>? onClose = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Post(() => Alert(severity, title, message, onClose));
            return;
        }

        var alert = new AlertScope { Severity = severity, Title = title, Message = message };
        alert.OnClose = Action(() => CloseAlert(alert, onClose));
        Scope.Alert = alert;
    }

    private async Task CloseAlert(AlertScope alert, Func<Task>? onClose)
    {
        if (ReferenceEquals(Scope.Alert, alert))
        {
            Scope.Alert = null;
        }

        if (onClose is not null)
        {
            await onClose();
        }
    }

    private Task OnHome() => Flip(RootPlace);

    private Task OnOpenTodos() => Flip(Places.Todos);

    private Task OnOpenSubscriptions() => Flip(Places.Subscriptions);

    private void OnLogin() => Alert(AlertSeverity.Info, "Work in progress...", "No implementation to this action, yet.");

    private void OnOpenAlert(AlertSeverity severity) => Alert(severity, "Some title", "Some message");
}
