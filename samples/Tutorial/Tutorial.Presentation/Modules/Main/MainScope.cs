using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Main;

public sealed class MainScope : Scope
{
    public Scope? Body { get; set => Set(ref field, value); }

    public Scope? Dialog { get; set => Set(ref field, value); }

    public AlertScope? Alert { get; set => Set(ref field, value); }

    public Func<Task> OnHome { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnOpenTodos { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnOpenSubscriptions { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnLogin { get; set; } = ScopeActions.Unbound();
}
