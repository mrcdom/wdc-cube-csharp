namespace Wdc.Domain;

/// <summary>
/// Declares a <see cref="DataPolicy{TEntity, TCriteria}"/>: what the policy needs to know about the entity, then the
/// grants per role. <see cref="Build"/> checks that every criterion filters a field, and that every reference and
/// collection names the policy of what it points to, so a gap fails when the policy is built, not on a request.
/// </summary>
public sealed class DataPolicyBuilder<TEntity, TCriteria>
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    private readonly EntityModel<TEntity> _model;
    private readonly Func<TCriteria> _newCriteria;
    private readonly HashSet<string> _writeOnly = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _criterionPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IDataPolicy>> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _inverses = new(StringComparer.Ordinal);
    private readonly List<(string[] Roles, Action<DataGrantBuilder<TEntity, TCriteria>> Declare)> _grants = [];
    private Func<TCriteria, IEnumerable<string>> _orderings = _ => [];
    private Func<TEntity>? _defaultShape;

    internal DataPolicyBuilder(EntityModel<TEntity> model, Func<TCriteria> newCriteria)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(newCriteria);
        _model = model;
        _newCriteria = newCriteria;
    }

    /// <summary>
    /// Fields written but never read by anyone (a password): no grant may read or mask them, asking for one is
    /// refused even without security, and they are out of the default shape.
    /// </summary>
    public DataPolicyBuilder<TEntity, TCriteria> WriteOnly(params IEnumerable<string> fields)
    {
        foreach (var name in fields)
        {
            var field = Field(name);
            if (field.IsKey || field.Kind != FieldKind.Scalar)
            {
                throw new InvalidOperationException($"{_model.Name}.{name}: only a scalar field other than the key can be write-only.");
            }

            _writeOnly.Add(name);
        }

        return this;
    }

    /// <summary>
    /// A criterion whose name is not the field it filters (<c>userId</c> filters <c>user</c>), or that filters a field
    /// of another entity through a reference (<c>customerName</c> filters <c>user.name</c>: the actor must then read
    /// <c>user</c> here and <c>name</c> in the user's policy).
    /// </summary>
    public DataPolicyBuilder<TEntity, TCriteria> Criterion(string criterion, string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(criterion);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        _criterionPaths[criterion] = fieldPath;
        return this;
    }

    /// <summary>The fields each ordering sorts by; the key, a tie-breaker, need not be named.</summary>
    public DataPolicyBuilder<TEntity, TCriteria> Orderings(Func<TCriteria, IEnumerable<string>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _orderings = fields;
        return this;
    }

    /// <summary>A reference, read through the policy of the entity it points to (a function: policies refer to each other).</summary>
    public DataPolicyBuilder<TEntity, TCriteria> Reference<TOther, TOtherCriteria>(string field, Func<DataPolicy<TOther, TOtherCriteria>> policy)
        where TOther : class
        where TOtherCriteria : Criteria<TOtherCriteria, TOther>
    {
        ArgumentNullException.ThrowIfNull(policy);
        Require(field, FieldKind.Reference);
        _targets[field] = policy;
        return this;
    }

    /// <summary>
    /// A collection, read and created through the policy of its rows' entity.
    /// </summary>
    /// <param name="field">The collection field.</param>
    /// <param name="policy">The policy of its rows' entity.</param>
    /// <param name="inverse">The reference of each child that points back to the owner: set on the children of a new
    /// row while they are checked, since a child of a new row belongs to it.</param>
    public DataPolicyBuilder<TEntity, TCriteria> Collection<TOther, TOtherCriteria>(string field, string inverse,
        Func<DataPolicy<TOther, TOtherCriteria>> policy)
        where TOther : class
        where TOtherCriteria : Criteria<TOtherCriteria, TOther>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inverse);
        ArgumentNullException.ThrowIfNull(policy);
        Require(field, FieldKind.Collection);
        _targets[field] = policy;
        _inverses[field] = inverse;
        return this;
    }

    /// <summary>
    /// The shape a read without a projection gets; it may go into references and collections. A new projection on each
    /// call. Without it, the model's default fields but the collections and the write-only ones, references by key.
    /// </summary>
    public DataPolicyBuilder<TEntity, TCriteria> DefaultShape(Func<TEntity> shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        _defaultShape = shape;
        return this;
    }

    /// <summary>Adds a grant to each of <paramref name="roles"/>; a role may have several, one per set of rows.</summary>
    public DataPolicyBuilder<TEntity, TCriteria> Grant(IEnumerable<string> roles, Action<DataGrantBuilder<TEntity, TCriteria>> declare)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(declare);
        var list = roles.ToArray();
        if (list.Length == 0)
        {
            throw new ArgumentException($"{_model.Name}: a grant needs a role.", nameof(roles));
        }

        _grants.Add((list, declare));
        return this;
    }

    public DataPolicy<TEntity, TCriteria> Build()
    {
        foreach (var field in _model.Fields)
        {
            if (field.Kind != FieldKind.Scalar && !_targets.ContainsKey(field.Name))
            {
                throw new InvalidOperationException(
                    $"{_model.Name}.{field.Name} points to another entity: declare its policy with {(field.Kind == FieldKind.Reference ? "Reference" : "Collection")}(...).");
            }
        }

        var unknown = _newCriteria().Criterions
            .Select(c => c.Name)
            .Where(c => _model.Find(PolicyEntity<TEntity, TCriteria>.Head(_criterionPaths.GetValueOrDefault(c, c))) is null)
            .ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"{_model.Name}: criteria fields with no entity field: [{string.Join(", ", unknown)}]; map them with Criterion(...).");
        }

        var entity = new PolicyEntity<TEntity, TCriteria>(_model, _newCriteria, _writeOnly, _criterionPaths, _orderings,
            _defaultShape, _targets, _inverses);
        var byRole = new Dictionary<string, List<DataGrant<TEntity, TCriteria>>>(StringComparer.Ordinal);
        foreach (var (roles, declare) in _grants)
        {
            var builder = new DataGrantBuilder<TEntity, TCriteria>(entity);
            declare(builder);
            var grant = builder.Build();
            foreach (var role in roles)
            {
                if (!byRole.TryGetValue(role, out var grants))
                {
                    byRole[role] = grants = [];
                }

                grants.Add(grant);
            }
        }

        return new DataPolicy<TEntity, TCriteria>(entity, byRole);
    }

    private FieldModel<TEntity> Field(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _model.Find(name) ?? throw new ArgumentException($"{_model.Name} has no field '{name}'.", nameof(name));
    }

    private void Require(string name, FieldKind kind)
    {
        if (Field(name).Kind != kind)
        {
            throw new InvalidOperationException($"{_model.Name}.{name} is not a {kind.ToString().ToLowerInvariant()}.");
        }
    }
}
