using Wdc.Cube;

namespace MyCubeApp;

/// <summary>The page frame: what the place below the application shows, and an error no screen handled.</summary>
public sealed class RootScope : Scope
{
    public Scope? Body { get; set => Set(ref field, value); }

    public Message? ErrorMessage { get; set => Set(ref field, value); }

    public Func<Task> OnDismissError { get; set; } = ScopeActions.Unbound();
}
