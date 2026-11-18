using Wdc.Cube;

namespace Tutorial.Presentation.Modules.TodoMvc;

public sealed class TodoMvcScope : Scope
{
    public HeaderScope? Header { get; set => Set(ref field, value); }

    public TodoListScope? List { get; set => Set(ref field, value); }

    public FooterScope? Footer { get; set => Set(ref field, value); }

    /// <summary>True when the list came from the 1000-item generator.</summary>
    public bool StressMode { get; set => Set(ref field, value); }

    public Func<Task> OnToggleStress { get; set; } = ScopeActions.Unbound();
}
