namespace Wdc.Domain;

/// <summary>
/// Who may do what with the data of an entity: per role, the rows it reaches, the fields it reads (as they are or
/// masked), the fields it writes, and whether it deletes.
/// </summary>
/// <remarks>
/// <para>The model is <b>allow-wins</b>: an actor with several roles gets every grant of every role, and a request is
/// allowed when some grant allows it. Fields are decided <b>per row</b>: a row shows what the grants that reach it
/// allow, so reading everyone's name in one role and one's own document in another does not add up to everyone's
/// document.</para>
/// <code>
/// public static readonly DataPolicy&lt;User, UserCriteria&gt; Policy = DataPolicy.For(User.Model, () => new UserCriteria())
///     .WriteOnly("password")
///     .Criterion("userId", "id")
///     .Grant(["Admin"], g => g.Read("userName", "name").Update("name").Delete())
///     .Grant(["Customer"], g => g.Rows(OwnUser.Scope).Read("userName", "name"))
///     .Build();
/// </code>
/// </remarks>
public sealed class DataPolicy<TEntity, TCriteria> : IDataPolicy
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    private readonly Dictionary<string, List<DataGrant<TEntity, TCriteria>>> _grantsByRole;

    internal DataPolicy(PolicyEntity<TEntity, TCriteria> entity, Dictionary<string, List<DataGrant<TEntity, TCriteria>>> grantsByRole)
    {
        Entity = entity;
        _grantsByRole = grantsByRole;
    }

    public EntityModel<TEntity> Model => Entity.Model;

    internal PolicyEntity<TEntity, TCriteria> Entity { get; }

    /// <summary>The grants of a role; empty when it has none on this entity.</summary>
    public IReadOnlyList<DataGrant<TEntity, TCriteria>> GrantsOf(string role) =>
        _grantsByRole.TryGetValue(role, out var grants) ? grants : [];

    /// <summary>
    /// What <paramref name="actor"/> may do. Without an actor (no security: local or test mode) every row and field is
    /// reached and every write passes, but for the write-only fields, which nobody reads; a read without a projection
    /// keeps the repository's own default.
    /// </summary>
    public DataAccess<TEntity, TCriteria> AccessFor(Actor? actor)
    {
        if (actor is null)
        {
            return new DataAccess<TEntity, TCriteria>(Entity, null, [Unrestricted()]);
        }

        var grants = new List<DataGrant<TEntity, TCriteria>>();
        foreach (var role in actor.Roles)
        {
            foreach (var grant in GrantsOf(role))
            {
                if (!grants.Exists(g => ReferenceEquals(g, grant))) // a grant shared by two roles counts once
                {
                    grants.Add(grant);
                }
            }
        }

        return new DataAccess<TEntity, TCriteria>(Entity, actor, grants);
    }

    /// <summary>Without security: every row, every field but the write-only ones read; every write allowed.</summary>
    private DataGrant<TEntity, TCriteria> Unrestricted()
    {
        var all = Entity.Fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var readable = all.Where(n => !Entity.IsWriteOnly(n)).ToHashSet(StringComparer.Ordinal);
        return new DataGrant<TEntity, TCriteria>(RowScope.All<TEntity, TCriteria>(), readable,
            new Dictionary<string, Mask>(), all, all, true);
    }

    string IDataPolicy.Name => Entity.Name;

    object IDataPolicy.KeyProjection() => Model.KeyProjection();

    object IDataPolicy.NewProjectionList(object shape, ICriteria? criteria, int? limit, int? offset) =>
        new ProjectionList<TEntity>((TEntity)shape, criteria, limit, offset);

    ICriteria IDataPolicy.NewCriteria() => Entity.NewCriteria();

    bool IDataPolicy.HasFieldsSet(object? entity) => Entity.FieldsSetIn((TEntity?)entity).Count > 0;

    void IDataPolicy.KeepKeyOnly(object row) => Entity.KeepKeyOnly((TEntity)row);

    object IDataPolicy.ProjectionMirroring(object row, HashSet<object> seen) => Entity.ProjectionMirroring((TEntity)row, seen);

    void IDataPolicy.Merge(object into, object? from) => Entity.Merge((TEntity)into, (TEntity?)from);

    object? IDataPolicy.GetField(object row, string name) => Entity.Field(name).GetValue((TEntity)row);

    void IDataPolicy.SetField(object row, string name, object? value) => Entity.Field(name).SetValue((TEntity)row, value);

    IDataAccess IDataPolicy.AccessFor(Actor? actor) => AccessFor(actor);
}

/// <summary>Declaring data policies.</summary>
public static class DataPolicy
{
    /// <summary>Starts the policy of the entity <paramref name="model"/> describes, filtered by <typeparamref name="TCriteria"/>.</summary>
    public static DataPolicyBuilder<TEntity, TCriteria> For<TEntity, TCriteria>(EntityModel<TEntity> model, Func<TCriteria> newCriteria)
        where TEntity : class
        where TCriteria : Criteria<TCriteria, TEntity> => new(model, newCriteria);
}

/// <summary>A data policy seen without its types: how the policy of one entity reaches the ones it points to.</summary>
internal interface IDataPolicy
{
    string Name { get; }

    object KeyProjection();

    object NewProjectionList(object shape, ICriteria? criteria, int? limit, int? offset);

    ICriteria NewCriteria();

    bool HasFieldsSet(object? entity);

    void KeepKeyOnly(object row);

    object ProjectionMirroring(object row, HashSet<object> seen);

    void Merge(object into, object? from);

    object? GetField(object row, string name);

    void SetField(object row, string name, object? value);

    IDataAccess AccessFor(Actor? actor);
}
