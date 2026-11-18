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
