using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Restricted;

public sealed class RestrictedScope : Scope
{
    /// <summary>Filled by the slot this presenter hands to a deeper place.</summary>
    public Scope? Detail { get; set => Set(ref field, value); }
}
