namespace Wdc.Domain;

/// <summary>
/// Declares a <see cref="DataGrant{TEntity, TCriteria}"/>. Fields are named as in the entity model; each name is
/// checked when the grant is built, and what would contradict the policy is refused: masking a field that is not text,
/// or the key; reading a field both ways; writing a field the same grant masks; reading a write-only field; writing the
/// key.
/// </summary>
public sealed class DataGrantBuilder<TEntity, TCriteria>
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    private readonly PolicyEntity<TEntity, TCriteria> _entity;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mask> _masked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _create = new(StringComparer.Ordinal);
    private readonly HashSet<string> _update = new(StringComparer.Ordinal);
    private IRowScope<TEntity, TCriteria> _rows = RowScope.All<TEntity, TCriteria>();
    private bool _delete;

    internal DataGrantBuilder(PolicyEntity<TEntity, TCriteria> entity) => _entity = entity;

    /// <summary>The rows the grant reaches; every row when not declared.</summary>
    public DataGrantBuilder<TEntity, TCriteria> Rows(IRowScope<TEntity, TCriteria> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _rows = scope;
        return this;
    }

    /// <summary>Fields read as they are, and usable in filters and orderings. The key needs no declaring: it is always read.</summary>
    public DataGrantBuilder<TEntity, TCriteria> Read(params IEnumerable<string> fields)
    {
        foreach (var name in Readable(fields))
        {
            _read.Add(name);
        }

        return this;
    }

    /// <summary>Fields read through <paramref name="mask"/>: text fields only, never the key.</summary>
    public DataGrantBuilder<TEntity, TCriteria> Mask(Mask mask, params IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(mask);
        foreach (var name in Readable(fields))
        {
            var field = _entity.Field(name);
            if (field.IsKey)
            {
                throw new InvalidOperationException($"{_entity.Name}.{name} is the key, always read as it is, and cannot be masked.");
            }

            if (field.Kind != FieldKind.Scalar || !ReferenceEquals(field.ScalarKind, ValueKinds.String))
            {
                throw new InvalidOperationException($"{_entity.Name}.{name} is not text and cannot be masked.");
            }

            _masked[name] = mask;
        }

        return this;
    }

    /// <summary>Fields that may be set when creating a row.</summary>
    public DataGrantBuilder<TEntity, TCriteria> Create(params IEnumerable<string> fields)
    {
        _create.UnionWith(Writable(fields));
        return this;
    }

    /// <summary>Fields that may be changed in a reachable row.</summary>
    public DataGrantBuilder<TEntity, TCriteria> Update(params IEnumerable<string> fields)
    {
        _update.UnionWith(Writable(fields));
        return this;
    }

    /// <summary>The reachable rows may be deleted.</summary>
    public DataGrantBuilder<TEntity, TCriteria> Delete()
    {
        _delete = true;
        return this;
    }

    internal DataGrant<TEntity, TCriteria> Build()
    {
        foreach (var name in _masked.Keys)
        {
            if (_read.Contains(name))
            {
                throw new InvalidOperationException($"{_entity.Name}.{name} is declared both read and masked.");
            }

            if (_create.Contains(name) || _update.Contains(name))
            {
                throw new InvalidOperationException($"{_entity.Name}.{name} is masked and cannot be written by the same grant.");
            }
        }

        return new DataGrant<TEntity, TCriteria>(_rows, _read.ToHashSet(StringComparer.Ordinal),
            new Dictionary<string, Mask>(_masked, StringComparer.Ordinal), _create.ToHashSet(StringComparer.Ordinal),
            _update.ToHashSet(StringComparer.Ordinal), _delete);
    }

    private List<string> Readable(IEnumerable<string> fields)
    {
        var names = Names(fields);
        foreach (var name in names)
        {
            if (_entity.IsWriteOnly(name))
            {
                throw new InvalidOperationException($"{_entity.Name}.{name} is write-only and cannot be read.");
            }
        }

        return names;
    }

    private List<string> Writable(IEnumerable<string> fields)
    {
        var names = Names(fields);
        foreach (var name in names)
        {
            if (_entity.Field(name).IsKey)
            {
                throw new InvalidOperationException($"{_entity.Name}.{name} is the key, which identifies the row and is not written.");
            }
        }

        return names;
    }

    private List<string> Names(IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var names = fields.ToList();
        foreach (var name in names)
        {
            _ = _entity.Field(name); // an unknown name fails here, when the policy is built
        }

        return names;
    }
}
