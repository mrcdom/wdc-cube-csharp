using Wdc.Cube;

namespace Tutorial.Presentation.Modules;

public enum AlertSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A scope shown in the dialog slot: it must know how to close.</summary>
public interface IDialogScope
{
    Func<Task> OnClose { get; }
}

public sealed class AlertScope : Scope
{
    public AlertSeverity Severity { get; set => Set(ref field, value); }

    public string? Title { get; set => Set(ref field, value); }

    public string? Message { get; set => Set(ref field, value); }

    public Func<Task> OnClose { get; set; } = ScopeActions.Unbound();
}

/// <summary>The home body, shown when no module is open.</summary>
public sealed class BodyScope : Scope
{
    public Func<AlertSeverity, Task> OnOpenAlert { get; set; } = ScopeActions.Unbound<AlertSeverity>();
}

public sealed class RootScope : Scope
{
    public Scope? Body { get; set => Set(ref field, value); }

    public Scope? Dialog { get; set => Set(ref field, value); }

    public AlertScope? Alert { get; set => Set(ref field, value); }

    public Func<Task> OnHome { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnOpenTodos { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnOpenSubscriptions { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnLogin { get; set; } = ScopeActions.Unbound();
}
