using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Main;

/// <summary>The home body, shown when no module is open.</summary>
public sealed class BodyScope : Scope
{
    public Func<AlertSeverity, Task> OnOpenAlert { get; set; } = ScopeActions.Unbound<AlertSeverity>();
}
