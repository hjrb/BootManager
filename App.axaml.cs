using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BootManager.Services;
using BootManager.ViewModels;
using BootManager.Views;
using Microsoft.Extensions.Configuration;

namespace BootManager;

/// <summary>
/// The Avalonia application object, responsible for loading the shared styles and creating the main
/// window together with the platform services it depends on.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The merged configuration (appsettings.json, environment variables, command line).
    /// </summary>
    /// <remarks>
    /// Assigned by <c>Program.Main</c> before the UI starts, which is why it is declared as non-nullable
    /// with the null-forgiving initialiser: by the time any UI code can read it, it has been set.
    /// </remarks>
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
                DataContext = new MainViewModel(
                    BootManagerServiceFactory.Create(),
                    BootManagerServiceFactory.CreateSystemInfoService()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}