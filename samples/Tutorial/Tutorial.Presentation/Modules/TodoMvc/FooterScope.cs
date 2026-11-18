using Wdc.Cube;

namespace Tutorial.Presentation.Modules.TodoMvc;

public sealed class FooterScope : Scope
{
    public int Count { get; set => Set(ref field, value); }

    public string ActiveTodoWord { get; set => Set(ref field, value); } = "item";

    public bool ClearButtonVisible { get; set => Set(ref field, value); }

    public ShowingOptions Showing { get; set => Set(ref field, value); }

    public Func<Task> OnClearCompleted { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnShowAll { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnShowActives { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnShowCompleteds { get; set; } = ScopeActions.Unbound();
}
