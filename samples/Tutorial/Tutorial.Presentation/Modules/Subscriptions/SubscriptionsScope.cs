using Tutorial.Presentation.Services;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Subscriptions;

public sealed class SubscriptionsScope : Scope
{
    public IReadOnlyList<SiteItem> Sites { get; set => Set(ref field, value); } = [];

    public bool Loading { get; set => Set(ref field, value); }

    public Func<SiteItem, Task> OnItemClicked { get; set; } = ScopeActions.Unbound<SiteItem>();
}
