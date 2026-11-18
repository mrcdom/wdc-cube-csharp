using System.Runtime.CompilerServices;

namespace Wdc.Cube;

/// <summary>
/// Placeholders for the actions a scope declares, to be replaced by the presenter:
/// <code>public Func&lt;Task&gt; OnSave { get; set; } = ScopeActions.Unbound();</code>
/// </summary>
/// <remarks>
/// A placeholder never throws: an action declared and never bound is a button that does nothing. It reports itself
/// through <see cref="System.Diagnostics.Trace"/> when called, and <see cref="IsUnbound"/> lets a test assert that the
/// presenter bound everything.
/// </remarks>
public static class ScopeActions
{
    private static readonly ConditionalWeakTable<Delegate, string> s_unbound = [];

    /// <summary>A no-op action that reports being called.</summary>
    public static Func<Task> Unbound([CallerMemberName] string? name = null) =>
        Register<Func<Task>>(() => Called(name), name);

    /// <summary>A no-op action with one argument that reports being called.</summary>
    public static Func<T, Task> Unbound<T>([CallerMemberName] string? name = null) =>
        Register<Func<T, Task>>(_ => Called(name), name);

    /// <summary>A no-op action with two arguments that reports being called.</summary>
    public static Func<T1, T2, Task> Unbound<T1, T2>([CallerMemberName] string? name = null) =>
        Register<Func<T1, T2, Task>>((_, _) => Called(name), name);

    /// <summary>A no-op action with three arguments that reports being called.</summary>
    public static Func<T1, T2, T3, Task> Unbound<T1, T2, T3>([CallerMemberName] string? name = null) =>
        Register<Func<T1, T2, T3, Task>>((_, _, _) => Called(name), name);

    /// <summary>Whether <paramref name="action"/> is still a placeholder.</summary>
    public static bool IsUnbound(Delegate? action) => action is not null && s_unbound.TryGetValue(action, out _);

    private static TDelegate Register<TDelegate>(TDelegate action, string? name)
        where TDelegate : Delegate
    {
        s_unbound.AddOrUpdate(action, name ?? "?");
        return action;
    }

    private static Task Called(string? name)
    {
        System.Diagnostics.Trace.TraceWarning(
            $"Scope action '{name}' was called but never bound by its presenter: it does nothing.");
        return Task.CompletedTask;
    }
}
