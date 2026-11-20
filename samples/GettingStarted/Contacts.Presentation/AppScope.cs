using Wdc.Cube;

namespace Contacts;

// <snippet:app-scope>
public sealed class AppScope : Scope
{
    /// <summary>What the place below the application shows.</summary>
    public Scope? Body { get; set => Set(ref field, value); }
}
// </snippet:app-scope>
