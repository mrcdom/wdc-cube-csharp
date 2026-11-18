namespace Wdc.Rest.Server;

/// <summary>An operation of the repository routes, for authorization.</summary>
public enum RpcOperation
{
    Insert,
    Update,
    Delete,
    Count,
    Fetch,
    FetchPage,
    FetchById,
}
