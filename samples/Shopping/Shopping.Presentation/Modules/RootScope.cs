using Wdc.Cube;

namespace Shopping.Presentation.Modules;

public sealed class RootScope : Scope
{
    public Scope? Body { get; set => Set(ref field, value); }

    /// <summary>The global error toast; it goes away on its own.</summary>
    public string? ErrorMessage { get; set => Set(ref field, value); }

    public Func<Task> OnDismissError { get; set; } = ScopeActions.Unbound();
}
