using Wdc.Cube;

namespace Contacts;

// <snippet:root-scope>
public sealed class RootScope : Scope
{
    /// <summary>What the place below the application shows.</summary>
    public Scope? Body { get; set => Set(ref field, value); }
}
// </snippet:root-scope>
