using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Login;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class LoginView : ScopeView
{
    public LoginView() => InitializeComponent();

    private LoginScope S => (LoginScope)Scope!;

    // What is being typed: view plumbing. The presenter receives it on Enter; the password never goes to the scope.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Scope is LoginScope { UserName: { } user })
        {
            UserBox.Text = user;
        }
    }

    private void OnEnter(object? sender, RoutedEventArgs e) => _ = S.OnEnter(UserBox.Text ?? string.Empty, PasswordBox.Text ?? string.Empty);

    private void OnPasswordKey(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == global::Avalonia.Input.Key.Enter)
        {
            OnEnter(sender, e);
        }
    }
}
