using Avalonia.Controls;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Main;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class MainView : ScopeView
{
    public MainView() => InitializeComponent();

    private MainScope S => (MainScope)Scope!;

    private void OnDismiss(object? sender, global::Avalonia.Input.PointerPressedEventArgs e) => _ = S.OnDismissError();
}
