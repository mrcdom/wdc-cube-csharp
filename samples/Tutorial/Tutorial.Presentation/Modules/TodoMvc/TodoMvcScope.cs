using Wdc.Cube;

namespace Tutorial.Presentation.Modules.TodoMvc;

public sealed class HeaderScope : Scope
{
    public bool AllItemsCompleted { get; set => Set(ref field, value); }

    public bool ToggleButtonVisible { get; set => Set(ref field, value); }

    public string InputValue { get; set => Set(ref field, value); } = string.Empty;

    /// <summary>Typing mirror: not guarded.</summary>
    public Func<string, Task> OnInputChange { get; set; } = ScopeActions.Unbound<string>();

    /// <summary>A key pressed in the input (<c>Enter</c>, <c>Escape</c>...).</summary>
    public Func<string, Task> OnInputKeyDown { get; set; } = ScopeActions.Unbound<string>();

    public Func<Task> OnToggleAll { get; set; } = ScopeActions.Unbound();
}

public sealed class ClockScope : Scope
{
    public DateTimeOffset Date { get; set => Set(ref field, value); }
}

public sealed class ItemScope : Scope
{
    public long Id { get; set => Set(ref field, value); }

    public bool Completed { get; set => Set(ref field, value); }

    public bool Editing { get; set => Set(ref field, value); }

    public string Title { get; set => Set(ref field, value); } = string.Empty;

    public Func<Task> OnDestroy { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnToggle { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnEdit { get; set; } = ScopeActions.Unbound();

    /// <summary>The edit box lost focus with this text.</summary>
    public Func<string, Task> OnBlur { get; set; } = ScopeActions.Unbound<string>();

    /// <summary>A key pressed in the edit box: (text, key).</summary>
    public Func<string, string, Task> OnKeyDown { get; set; } = ScopeActions.Unbound<string, string>();
}

/// <summary>The visible list (filtered) and the clock of the stress mode.</summary>
public sealed class TodoListScope : Scope
{
    public TodoListScope() => Items = new ScopeList<ItemScope>(this, nameof(Items));

    public ClockScope? Clock { get; set => Set(ref field, value); }

    public ScopeList<ItemScope> Items { get; }
}

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

public sealed class TodoMvcScope : Scope
{
    public HeaderScope? Header { get; set => Set(ref field, value); }

    public TodoListScope? List { get; set => Set(ref field, value); }

    public FooterScope? Footer { get; set => Set(ref field, value); }

    /// <summary>True when the list came from the 1000-item generator.</summary>
    public bool StressMode { get; set => Set(ref field, value); }

    public Func<Task> OnToggleStress { get; set; } = ScopeActions.Unbound();
}
