namespace Wdc.Domain;

/// <summary>The untyped view of a field, for code that describes a model without its CLR types (a schema).</summary>
public interface IFieldModel
{
    /// <summary>The name in JSON.</summary>
    string Name { get; }

    FieldKind Kind { get; }

    bool IsKey { get; }

    bool InDefaultProjection { get; }

    /// <summary>The value kind of a scalar field; <see langword="null"/> for references and collections.</summary>
    IValueKind? ScalarKind { get; }

    /// <summary>The model of the entity a reference or a collection holds; <see langword="null"/> for a scalar.</summary>
    IEntityModel? ChildModel { get; }

    /// <summary>Creates the child's criteria of a collection that declares one, or <see langword="null"/>.</summary>
    Func<ICriteria>? NewCriteria { get; }
}
