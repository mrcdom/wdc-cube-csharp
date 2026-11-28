namespace Wdc.Domain;

/// <summary>The untyped view of an entity model.</summary>
public interface IEntityModel
{
    Type EntityType { get; }

    string Name { get; }

    /// <summary>Every field, in declaration order.</summary>
    IReadOnlyList<IFieldModel> Fields { get; }

    /// <summary>The key field, when declared.</summary>
    IFieldModel? Key { get; }
}
