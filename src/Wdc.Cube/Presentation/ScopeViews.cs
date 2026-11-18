namespace Wdc.Cube;

/// <summary>Ready-made views.</summary>
public static class ScopeViews
{
    /// <summary>
    /// A view for technologies that draw synchronously and cannot tell when they finished: runs
    /// <paramref name="render"/> and confirms right away.
    /// </summary>
    public static IScopeView Immediate(Action<Scope> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        return new ImmediateView(render);
    }

    private sealed class ImmediateView(Action<Scope> render) : IScopeView
    {
        public void Invalidate(Scope scope)
        {
            try
            {
                render(scope);
            }
            finally
            {
                scope.MarkRendered();
            }
        }
    }
}
