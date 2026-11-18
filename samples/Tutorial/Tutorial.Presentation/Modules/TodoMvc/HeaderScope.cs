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
