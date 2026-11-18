using Tutorial.Presentation.Modules.Main;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Subscriptions;

public sealed class SubscriptionsDetailScope : Scope, IDialogScope
{
    /// <summary>The site being subscribed to. Shown; never typed into.</summary>
    public string? Site { get; set => Set(ref field, value); }

    /// <summary>What the reader typed. Empty until they do.</summary>
    public string Email { get; set => Set(ref field, value); } = string.Empty;

    public Func<Task> OnClose { get; set; } = ScopeActions.Unbound();

    public Func<Task> OnSubscribe { get; set; } = ScopeActions.Unbound();

    public Func<string, Task> OnEmailChanged { get; set; } = ScopeActions.Unbound<string>();
}
