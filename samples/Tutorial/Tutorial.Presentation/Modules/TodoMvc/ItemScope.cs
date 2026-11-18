using Wdc.Cube;

namespace Tutorial.Presentation.Modules.TodoMvc;

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
