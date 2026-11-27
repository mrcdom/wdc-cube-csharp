namespace Wdc.Domain;

/// <summary>The untyped view of a criteria object, for codecs and repositories.</summary>
public interface ICriteria
{
    /// <summary>Every criterion, in declaration order.</summary>
    IReadOnlyList<Criterion> Criterions { get; }

    /// <summary>
    /// Whether the criteria narrows the rows: some criterion asks for something, or every alternative group does (a
    /// delete with no filter is refused).
    /// </summary>
    bool HasFilter { get; }

    /// <summary>
    /// The alternative groups: the criteria matches when its own criterions match <b>and</b> at least one group
    /// matches. Each group is a criteria of the same entity, its criterions in AND, and may have groups of its own.
    /// Empty means no alternative; a group that asks nothing matches every row. Only a group's filter counts: its
    /// projection and ordering are ignored.
    /// </summary>
    /// <remarks>
    /// For code on the server (a policy restricting the rows each role reaches): the groups do not travel, and
    /// <see cref="CriteriaCodec"/> refuses them both ways. A term searched in several columns is a criterion of its
    /// own, which the repository translates over those columns.
    /// </remarks>
    IReadOnlyList<ICriteria> AnyOf { get; }

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
