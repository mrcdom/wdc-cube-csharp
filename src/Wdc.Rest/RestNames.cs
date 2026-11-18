namespace Wdc.Rest;

/// <summary>Names on the wire, shared with the Kotlin version (decision D-18).</summary>
public static class RestNames
{
    /// <summary>The remote transaction a request belongs to.</summary>
    public const string TransactionHeader = "X-Tx-Id";

    /// <summary>Identifies one client instance (one app on one device); part of a remote transaction's owner.</summary>
    public const string ClientHeader = "X-Client-Id";

    public const string Insert = "insert";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Count = "count";
    public const string Fetch = "fetch";
    public const string FetchPage = "fetch-page";
    public const string FetchById = "fetch-by-id";

    public const string TransactionBase = "/api/tx";
}
