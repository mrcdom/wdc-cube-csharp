using Shopping.Presentation.Modules;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class RootView : ScopeView
{
    public RootView() => InitializeComponent();

    private RootScope S => (RootScope)Scope!;

    private void OnDismiss(object? sender, global::Avalonia.Input.PointerPressedEventArgs e) => _ = S.OnDismissError();
}
