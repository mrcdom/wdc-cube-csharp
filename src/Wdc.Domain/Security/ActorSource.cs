namespace Wdc.Domain;

/// <summary>An <see cref="IActorSource"/> whose actor is set by its owner (a login, a request, a test).</summary>
public sealed class ActorSource : IActorSource
{
    public ActorSource(Actor? current = null) => Current = current;

    public Actor? Current { get; set; }
}
