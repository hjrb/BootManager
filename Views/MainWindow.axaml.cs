using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using BootManager.ViewModels;

namespace BootManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnCopyNotification(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { NotificationMessage: { Length: > 0 } message }
            && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(message);
        }
    }
}