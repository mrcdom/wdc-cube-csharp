namespace Wdc.Domain;

/// <summary>An operation of a repository, for authorization.</summary>
public enum RepositoryOperation
{
    Insert,
    Update,
    Delete,
    Count,
    Fetch,
    FetchPage,
    FetchById,
}
