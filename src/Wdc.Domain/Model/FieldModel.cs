using System.Text.Json;

namespace Wdc.Domain;

/// <summary>One field of an entity model.</summary>
public abstract class FieldModel<TEntity>
    where TEntity : class
{
    protected FieldModel(string name, FieldKind kind, bool isKey, bool inDefault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Kind = kind;
        IsKey = isKey;
        InDefaultProjection = inDefault;
    }

    /// <summary>The name in JSON (and, by default, how the SQL layer refers to it).</summary>
    public string Name { get; }

    public FieldKind Kind { get; }

    public bool IsKey { get; }

    public bool InDefaultProjection { get; }

    /// <summary>Whether the field holds a value (in a result) or is marked (in a projection).</summary>
    public abstract bool HasValue(TEntity entity);

    /// <summary>The value, boxed.</summary>
    public abstract object? GetValue(TEntity entity);

    /// <summary>Sets the value, boxed (a child entity or list for references and collections).</summary>
    public abstract void SetValue(TEntity entity, object? value);

    /// <summary>Copies this field's value from <paramref name="source"/> to <paramref name="target"/>.</summary>
    public void Copy(TEntity source, TEntity target) => SetValue(target, GetValue(source));

    /// <summary>Marks the field in a projection: a marker, a key-only child, or a list holding the child's own fields.</summary>
    public abstract void MarkOwnFields(TEntity projection);

    /// <summary>Whether this field differs between two beans (references compare by key; collections are not compared).</summary>
    public abstract bool Differs(TEntity next, TEntity previous);

    /// <summary>The value kind of a scalar field; <see langword="null"/> for references and collections.</summary>
    public virtual IValueKind? ScalarKind => null;

    /// <summary>Copies this field's mark into a write projection (nested shapes get their keys: see <see cref="EntityModel{TEntity}.WriteProjection"/>).</summary>
    internal virtual void CopyForWrite(TEntity from, TEntity to) => Copy(from, to);

    internal abstract void WriteJson(Utf8JsonWriter writer, TEntity entity, TEntity? projection, HashSet<object> visited);

    internal abstract void ReadJson(ref Utf8JsonReader reader, TEntity entity, TEntity? presence);
}
