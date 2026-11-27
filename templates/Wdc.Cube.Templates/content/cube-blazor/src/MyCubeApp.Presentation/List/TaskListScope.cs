using Wdc.Cube;

namespace MyCubeApp.List;

/// <summary>
/// The list screen: what its view draws and what it can ask for. A property set through <c>Set</c> asks for a repaint
/// when it changes; the actions are bound by the presenter.
/// </summary>
public sealed class TaskListScope : Scope
{
    public TaskListScope() => Rows = new ScopeList<TaskRowScope>(this, nameof(Rows));

    /// <summary>One row per task, each a scope of its own: a row that changes repaints alone.</summary>
    public ScopeList<TaskRowScope> Rows { get; }

    /// <summary>How many are not done: derived from the rows, once per repaint.</summary>
    public int Remaining { get; set => Set(ref field, value); }

    /// <summary>The detail of the task open (the place below the list), or null.</summary>
    public Scope? Detail { get; set => Set(ref field, value); }

    public Message? ErrorMessage { get; set => Set(ref field, value); }

    /// <summary>Adds a task with this title.</summary>
    public Func<string, Task> OnAdd { get; set; } = ScopeActions.Unbound<string>();
}

/// <summary>A row of the list. The scopes of one screen live in the same file.</summary>
public sealed class TaskRowScope : Scope
{
    public long Id { get; set => Set(ref field, value); }

    public string Title { get; set => Set(ref field, value); } = string.Empty;

    public bool Done { get; set => Set(ref field, value); }

    public Func<Task> OnToggle { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnOpen { get; set; } = ScopeActions.Unbound();
}
