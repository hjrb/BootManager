using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BootManager.Services;
using BootManager.ViewModels;
using BootManager.Views;
using Microsoft.Extensions.Configuration;

namespace BootManager;

public partial class App : Application
{
    public static IConfiguration Configuration { get; set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(BootManagerServiceFactory.Create()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}