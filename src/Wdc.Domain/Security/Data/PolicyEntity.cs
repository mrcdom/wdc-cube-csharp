namespace Wdc.Domain;

/// <summary>
/// What a data policy knows about its entity beyond the model: the write-only fields, the entity field each criterion
/// filters, the fields each ordering sorts by, the policies of the references and collections, and the shape of a read
/// without a projection.
/// </summary>
internal sealed class PolicyEntity<TEntity, TCriteria>
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    private readonly HashSet<string> _writeOnly;
    private readonly Dictionary<string, string[]> _criterionPaths;
    private readonly Func<TCriteria, IEnumerable<string>> _orderings;
    private readonly Func<TEntity>? _defaultShape;
    private readonly Dictionary<string, Func<IDataPolicy>> _targets;
    private readonly Dictionary<string, string> _inverses;

    internal PolicyEntity(EntityModel<TEntity> model, Func<TCriteria> newCriteria, HashSet<string> writeOnly,
        Dictionary<string, string[]> criterionPaths, Func<TCriteria, IEnumerable<string>> orderings, Func<TEntity>? defaultShape,
        Dictionary<string, Func<IDataPolicy>> targets, Dictionary<string, string> inverses)
    {
        Model = model;
        NewCriteria = newCriteria;
        Key = model.Key ?? throw new InvalidOperationException($"{model.Name} has no key: a data policy needs one.");
        _writeOnly = writeOnly;
        _criterionPaths = criterionPaths;
        _orderings = orderings;
        _defaultShape = defaultShape;
        _targets = targets;
        _inverses = inverses;
    }

    public EntityModel<TEntity> Model { get; }

    public Func<TCriteria> NewCriteria { get; }

    public string Name => Model.Name;

    public FieldModel<TEntity> Key { get; }

    /// <summary>The fields other than the key, in declaration order.</summary>
    public IEnumerable<FieldModel<TEntity>> Fields => Model.Fields.Where(f => !f.IsKey);

    public FieldModel<TEntity> Field(string name) =>
        Model.Find(name) ?? throw new ArgumentException($"{Name} has no field '{name}'.", nameof(name));

    public bool IsWriteOnly(string name) => _writeOnly.Contains(name);

    public IDataPolicy Target(FieldModel<TEntity> field) =>
        _targets.TryGetValue(field.Name, out var target)
            ? target()
            : throw new InvalidOperationException($"{Name}.{field.Name} declares no policy for what it points to.");

    /// <summary>For a collection: the reference of each child that points back to the owner.</summary>
    public string Inverse(FieldModel<TEntity> field) =>
        _inverses.TryGetValue(field.Name, out var inverse)
            ? inverse
            : throw new InvalidOperationException($"{Name}.{field.Name} is not a collection.");

    /// <summary>The fields other than the key set in <paramref name="entity"/>: for a projection, what it asks for.</summary>
    public List<string> FieldsSetIn(TEntity? entity)
    {
        var names = new List<string>();
        if (entity is null)
        {
            return names;
        }

        foreach (var field in Fields)
        {
            if (field.HasValue(entity))
            {
                names.Add(field.Name);
            }
        }

        return names;
    }

    /// <summary>Marks the field in a projection: a scalar's marker; the key of a reference, or of a collection's rows.</summary>
    public void Mark(FieldModel<TEntity> field, TEntity projection)
    {
        switch (field.Kind)
        {
            case FieldKind.Scalar:
                field.MarkOwnFields(projection);
                break;
            case FieldKind.Reference:
                field.SetValue(projection, Target(field).KeyProjection());
                break;
            default:
                var target = Target(field);
                field.SetValue(projection, target.NewProjectionList(target.KeyProjection(), null, null, null));
                break;
        }
    }

    /// <summary>A projection with the key and the given fields, references and collections by key.</summary>
    public TEntity ProjectionOf(IEnumerable<string> names)
    {
        var projection = Model.KeyProjection();
        foreach (var name in names)
        {
            Mark(Field(name), projection);
        }

        return projection;
    }

    /// <summary>
    /// A projection asking for what <paramref name="row"/> carries, into its references and collections. A row met
    /// on the way already (a child pointing back to its owner) is asked by key.
    /// </summary>
    public TEntity ProjectionMirroring(TEntity row, HashSet<object> seen)
    {
        seen.Add(row);
        var projection = Model.KeyProjection();
        foreach (var name in FieldsSetIn(row))
        {
            var field = Field(name);
            var value = field.GetValue(row);
            switch (field.Kind)
            {
                case FieldKind.Scalar:
                    Mark(field, projection);
                    break;
                case FieldKind.Reference:
                    if (value is null || seen.Contains(value))
                    {
                        Mark(field, projection);
                    }
                    else
                    {
                        field.SetValue(projection, Target(field).ProjectionMirroring(value, seen));
                    }

                    break;
                default:
                    var first = (value as System.Collections.IList) is { Count: > 0 } list ? list[0] : null;
                    if (first is null)
                    {
                        Mark(field, projection);
                    }
                    else
                    {
                        var target = Target(field);
                        field.SetValue(projection, target.NewProjectionList(target.ProjectionMirroring(first, seen), null, null, null));
                    }

                    break;
            }
        }

        return projection;
    }

    /// <summary>
    /// Adds to <paramref name="into"/> what <paramref name="from"/> asks for, into the references: how the fields a row
    /// scope reads join a read. A row scope does not reach through a collection.
    /// </summary>
    public void Merge(TEntity into, TEntity? from)
    {
        if (from is null)
        {
            return;
        }

        foreach (var name in FieldsSetIn(from))
        {
            var field = Field(name);
            switch (field.Kind)
            {
                case FieldKind.Scalar:
                    Mark(field, into);
                    break;
                case FieldKind.Reference:
                    if (!field.HasValue(into))
                    {
                        Mark(field, into);
                    }

                    Target(field).Merge(field.GetValue(into)!, field.GetValue(from));
                    break;
                default:
                    throw new InvalidOperationException($"{Name}.{name} is a collection: a row scope cannot read through it.");
            }
        }
    }

    /// <summary>Clears every field but the key: a related row read by its key.</summary>
    public void KeepKeyOnly(TEntity row)
    {
        foreach (var field in Fields)
        {
            field.SetValue(row, null);
        }
    }

    /// <summary>
    /// What a read without a projection gets, before it is trimmed to what the actor may read: the declared shape, or
    /// the model's default fields but the collections and the write-only ones, references by key.
    /// </summary>
    public TEntity DefaultShape() => _defaultShape?.Invoke()
        ?? ProjectionOf(Fields.Where(f => f.InDefaultProjection && f.Kind != FieldKind.Collection && !IsWriteOnly(f.Name))
            .Select(f => f.Name));

    /// <summary>
    /// What the criteria filters or sorts by, other than the key: the paths of its own criterions, of its alternative
    /// groups, and of its ordering. A path goes into a reference with dots (<c>user.name</c>).
    /// </summary>
    public List<string> FilteredPaths(TCriteria criteria)
    {
        var paths = new List<string>();
        Collect(criteria, paths);
        foreach (var name in _orderings(criteria))
        {
            Add(paths, name);
        }

        paths.Remove(Key.Name);
        return paths;
    }

    private void Collect(TCriteria criteria, List<string> paths)
    {
        foreach (var criterion in criteria.Criterions)
        {
            if (criterion.IsSet)
            {
                foreach (var path in _criterionPaths.GetValueOrDefault(criterion.Name, [criterion.Name]))
                {
                    Add(paths, path);
                }
            }
        }

        foreach (var group in criteria.AnyOf)
        {
            Collect(group, paths);
        }
    }

    private static void Add(List<string> paths, string path)
    {
        if (!paths.Contains(path))
        {
            paths.Add(path);
        }
    }

    public static string Head(string path) => path.Split('.', 2)[0];
}
