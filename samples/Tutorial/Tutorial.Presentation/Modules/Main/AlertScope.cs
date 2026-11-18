using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Main;

public sealed class AlertScope : Scope
{
    public AlertSeverity Severity { get; set => Set(ref field, value); }

    public string? Title { get; set => Set(ref field, value); }

    public string? Message { get; set => Set(ref field, value); }

    public Func<Task> OnClose { get; set; } = ScopeActions.Unbound();
}
