using Tutorial.Presentation.Modules.Main;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Restricted;

public sealed class RestrictedPresenter(MainPresenter app) : CubePresenter<MainPresenter, RestrictedScope>(app, new RestrictedScope())
{
    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new RestrictedKeys(App, intent);

        // Attributes come from the level above, which may change its mind between flips: read them every time.
        var parentSlot = keys.ParentSlot;
        if (last)
        {
            Scope.Detail = null;
        }
        else
        {
            keys.ParentSlot = detail => Scope.Detail = detail;
        }

        parentSlot(Scope);
        return Task.FromResult(true);
    }
}
