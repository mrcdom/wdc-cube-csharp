using Wdc.Cube;

namespace Tutorial.Presentation.Modules.TodoMvc;

public sealed class ClockScope : Scope
{
    public DateTimeOffset Date { get; set => Set(ref field, value); }
}
