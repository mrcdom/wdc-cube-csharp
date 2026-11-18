using System.Text;

namespace Wdc.Domain;

/// <summary>The untyped view of a criteria object, for codecs and repositories.</summary>
public interface ICriteria
{
    /// <summary>Every criterion, in declaration order.</summary>
    IReadOnlyList<Criterion> Criterions { get; }

    /// <summary>Whether some criterion asks for something (a delete with no filter is refused).</summary>
    bool HasFilter { get; }

    /// <summary>The ordering's wire name, or <see langword="null"/>.</summary>
    string? OrderByName { get; }

    /// <summary>The ordering names this criteria accepts.</summary>
    IReadOnlyList<string> AcceptedOrderBy { get; }

    /// <summary>Sets the ordering by wire name.</summary>
    /// <exception cref="InvalidRequestException">The name is not accepted.</exception>
    void SetOrderByName(string? name);

    /// <summary>The criterion named <paramref name="name"/>, or <see langword="null"/>.</summary>
    Criterion? Find(string name);
}

/// <summary>
/// The filter of one entity: one criterion per filterable field, declared in the constructor.
/// <code>
/// public sealed class ProductCriteria : Criteria&lt;ProductCriteria, Product, ProductOrderBy&gt;
/// {
///     public ProductCriteria()
///     {
///         Id = Comparable(ValueKinds.Int64, "id");
///         Name = Text("name");
///         Price = Comparable(ValueKinds.Decimal, "price");
///     }
///     public ComparableCriterion&lt;ProductCriteria, long?&gt; Id { get; }
///     public TextCriterion&lt;ProductCriteria&gt; Name { get; }
///     public ComparableCriterion&lt;ProductCriteria, decimal?&gt; Price { get; }
/// }
/// </code>
/// Requests on one field combine with AND (or OR after <c>Or()</c>); different fields always combine with AND.
/// </summary>
public abstract class Criteria<TSelf, TEntity> : ICriteria
    where TSelf : Criteria<TSelf, TEntity>
    where TEntity : class
{
    private readonly List<Criterion> _criterions = [];

    public IReadOnlyList<Criterion> Criterions => _criterions;

    public bool HasFilter => _criterions.Exists(c => c.IsSet);

    /// <summary>What to fetch (a bean filled with <see cref="Mark"/> values); the repository's default when null.</summary>
    public TEntity? Projection { get; set; }

    public virtual string? OrderByName => null;

    public virtual IReadOnlyList<string> AcceptedOrderBy => [];

    private TSelf Self => (TSelf)this;

    public TSelf WithProjection(TEntity? projection)
    {
        Projection = projection;
        return Self;
    }

    public virtual void SetOrderByName(string? name)
    {
        if (name is not null)
        {
            throw new InvalidRequestException($"unknown ordering: '{name}' — {GetType().Name} has none");
        }
    }

    public Criterion? Find(string name) => _criterions.Find(c => c.Name == name);

    public override string ToString() =>
        $"{GetType().Name}({string.Join("; ", _criterions.Where(c => c.IsSet))}{(OrderByName is { } o ? $"; orderBy {o}" : "")})";

    /// <summary>A field compared for identity only.</summary>
    protected Criterion<TSelf, T> Equality<T>(ValueKind<T> kind, string name) => Register(new Criterion<TSelf, T>(Self, name, kind));

    /// <summary>An ordered field.</summary>
    protected ComparableCriterion<TSelf, T> Comparable<T>(ValueKind<T> kind, string name) =>
        Register(new ComparableCriterion<TSelf, T>(Self, name, kind));

    /// <summary>A text field.</summary>
    protected TextCriterion<TSelf> Text(string name) => Register(new TextCriterion<TSelf>(Self, name));

    private TCriterion Register<TCriterion>(TCriterion criterion)
        where TCriterion : Criterion
    {
        if (Find(criterion.Name) is not null)
        {
            throw new ArgumentException($"{GetType().Name} declares '{criterion.Name}' twice.", nameof(criterion));
        }

        _criterions.Add(criterion);
        return criterion;
    }
}

/// <summary>
/// A criteria with a named ordering. The ordering is named by its effect (<c>CheapestFirst</c>), not by columns: the
/// repository turns the name into columns and adds the key as a tie-breaker. On the wire it travels as
/// <c>CHEAPEST_FIRST</c>, as in the Kotlin version.
/// </summary>
public abstract class Criteria<TSelf, TEntity, TOrderBy> : Criteria<TSelf, TEntity>
    where TSelf : Criteria<TSelf, TEntity, TOrderBy>
    where TEntity : class
    where TOrderBy : struct, Enum
{
    private static readonly (TOrderBy Value, string Wire)[] s_names =
        [.. Enum.GetValues<TOrderBy>().Select(v => (v, OrderByNames.ToWire(v.ToString())))];

    public TOrderBy? OrderBy { get; set; }

    public override string? OrderByName =>
        OrderBy is { } value ? Array.Find(s_names, n => EqualityComparer<TOrderBy>.Default.Equals(n.Value, value)).Wire : null;

    public override IReadOnlyList<string> AcceptedOrderBy => [.. s_names.Select(n => n.Wire)];

    public TSelf WithOrderBy(TOrderBy? orderBy)
    {
        OrderBy = orderBy;
        return (TSelf)this;
    }

    public override void SetOrderByName(string? name)
    {
        if (name is null)
        {
            OrderBy = null;
            return;
        }

        foreach (var (value, wire) in s_names)
        {
            if (wire == name)
            {
                OrderBy = value;
                return;
            }
        }

        // Unlike an unknown operator, an unknown ordering is refused: dropping it would show a list in some order
        // presented as the order asked for.
        throw new InvalidRequestException($"unknown ordering: '{name}' — accepted: {string.Join(", ", AcceptedOrderBy)}");
    }
}

internal static class OrderByNames
{
    /// <summary><c>CheapestFirst</c> → <c>CHEAPEST_FIRST</c>; <c>NameAToZ</c> → <c>NAME_A_TO_Z</c>.</summary>
    public static string ToWire(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                sb.Append('_');
            }

            sb.Append(char.ToUpperInvariant(name[i]));
        }

        return sb.ToString();
    }
}
