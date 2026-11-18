namespace Wdc.Domain;

/// <summary>The untyped view of an entity model.</summary>
public interface IEntityModel
{
    Type EntityType { get; }

    string Name { get; }
}
