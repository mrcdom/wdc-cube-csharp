using Wdc.Cube;

namespace Tutorial.Presentation.Modules.TodoMvc;

/// <summary>The visible list (filtered) and the clock of the stress mode.</summary>
public sealed class TodoListScope : Scope
{
    public TodoListScope() => Items = new ScopeList<ItemScope>(this, nameof(Items));

    public ClockScope? Clock { get; set => Set(ref field, value); }

    public ScopeList<ItemScope> Items { get; }
}
