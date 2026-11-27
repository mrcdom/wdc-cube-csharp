namespace Wdc.Domain;

/// <summary>A read: no transaction of its own (it reads in the caller's, if there is one).</summary>
public abstract class ResourceQuery<TResult> : ResourceCall<TResult>;
