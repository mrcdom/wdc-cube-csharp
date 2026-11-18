namespace Wdc.Domain;

/// <summary>Implemented by every entity: its key, for references, graphs and fetching by id.</summary>
public interface IKeyedEntity
{
    object? Key { get; }
}
