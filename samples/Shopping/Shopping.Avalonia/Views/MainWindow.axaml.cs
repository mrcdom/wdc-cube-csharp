using Avalonia.Controls;
using Avalonia.Input;
using Shopping.Presentation.Modules;
using Wdc.Cube;

namespace Shopping.Avalonia.Views;

/// <summary>The window: the application's scope, with no navigation bar; Alt+←/→ still walk the in-memory history.</summary>
public sealed partial class MainWindow : Window
{
    private readonly MemoryHistoryManager _history;

    public MainWindow()
        : this(null!, new MemoryHistoryManager())
    {
    }

    public MainWindow(RootPresenter presenter, MemoryHistoryManager history)
    {
        InitializeComponent();
        _history = history;
        Body.Content = presenter?.Scope;
        KeyDown += OnKey;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
        {
            _ = e.Key == Key.Left ? _history.GoBack() : _history.GoForward();
            e.Handled = true;
        }
    }
}
