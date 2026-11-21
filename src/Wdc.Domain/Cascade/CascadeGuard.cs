namespace Wdc.Domain;

/// <summary>The guard of the calling flow, if any (it follows <c>await</c>, like the ambient transaction).</summary>
public static class CascadeGuard
{
    private static readonly AsyncLocal<ICascadeGuard?> s_current = new();

    public static ICascadeGuard? Current => s_current.Value;

    /// <summary>Runs <paramref name="work"/> with <paramref name="guard"/> watching its cascade writes.</summary>
    public static async Task<T> Run<T>(ICascadeGuard guard, Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(work);
        s_current.Value = guard; // this flow only: the caller's value is back when we return
        return await work().ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="work"/> with no guard: what it writes in cascade is not reported.</summary>
    public static async Task Suspend(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        s_current.Value = null; // this flow only: the caller's guard is back when we return
        await work().ConfigureAwait(false);
    }
}
