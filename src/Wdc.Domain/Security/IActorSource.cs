namespace Wdc.Domain;

/// <summary>
/// Where a protected repository learns who is acting, at each call: the token of the request on a server, the session
/// of the circuit in Blazor Server. <see langword="null"/> means nobody, and nothing protected is served to nobody.
/// </summary>
public interface IActorSource
{
    Actor? Current { get; }
}
