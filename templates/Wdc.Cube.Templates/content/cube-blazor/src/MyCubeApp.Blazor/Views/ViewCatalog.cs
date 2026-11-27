using MyCubeApp.Detail;
using MyCubeApp.List;
using Wdc.Cube.Blazor;

namespace MyCubeApp.Blazor.Views;

/// <summary>Which component draws each scope that can land in a slot.</summary>
public static class ViewCatalog
{
    public static void Register(ViewRegistry views) => views
        .Register<RootScope, RootView>()
        .Register<TaskListScope, TaskListView>()
        .Register<TaskScope, TaskView>();
}
