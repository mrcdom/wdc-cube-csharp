using Wdc.Cube;

namespace MyCubeApp.Detail;

/// <summary>The detail of a task.</summary>
public sealed class TaskScope : Scope
{
    public long Id { get; set => Set(ref field, value); }

    public string Title { get; set => Set(ref field, value); } = string.Empty;

    public Message? ErrorMessage { get; set => Set(ref field, value); }

    /// <summary>Saves this title.</summary>
    public Func<string, Task> OnRename { get; set; } = ScopeActions.Unbound<string>();

    public Func<Task> OnRemove { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnClose { get; set; } = ScopeActions.Unbound();
}
