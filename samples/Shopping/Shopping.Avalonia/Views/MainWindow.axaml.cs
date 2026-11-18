using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Main;
using Wdc.Cube;

namespace Shopping.Avalonia.Views;

/// <summary>The window: back and forward over the in-memory history, and the application's scope.</summary>
public sealed partial class MainWindow : Window
{
    private readonly MemoryHistoryManager _history;

    public MainWindow()
        : this(null!, new MemoryHistoryManager())
    {
    }

    public MainWindow(MainPresenter presenter, MemoryHistoryManager history)
    {
        InitializeComponent();
        _history = history;
        Body.Content = presenter?.Scope;
        history.EntriesChanged += UpdateButtons;
        UpdateButtons();
        KeyDown += OnKey;
    }

    private void UpdateButtons()
    {
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;
    }

    private void OnBack(object? sender, RoutedEventArgs e) => _history.GoBack();

    private void OnForward(object? sender, RoutedEventArgs e) => _history.GoForward();

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
        {
            _ = e.Key == Key.Left ? _history.GoBack() : _history.GoForward();
            e.Handled = true;
        }
    }
}
