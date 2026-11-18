namespace Wdc.Rest;

/// <summary>The cut of a listing: <see cref="Offset"/>/<see cref="Limit"/> for <c>fetch</c>, <see cref="Page"/>/<see cref="PageSize"/> for <c>fetch-page</c>.</summary>
public readonly record struct Slice(int Offset = 0, int Limit = 0, int Page = 0, int PageSize = 0);
