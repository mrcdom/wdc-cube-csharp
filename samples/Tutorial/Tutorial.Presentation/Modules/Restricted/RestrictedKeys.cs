using Tutorial.Presentation.Modules.Main;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.Restricted;

public sealed class RestrictedKeys(CubeApplication app, FlipIntent? intent = null)
    : MainKeys(app, intent, Places.Restricted);
