using Avalonia;
using BootManager.Services;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Settings.Configuration;
using System;

namespace BootManager;

/// <summary>
/// Entry point of the application, which runs either as a desktop window or as a console tool
/// depending on the command line it was started with.
/// </summary>
sealed class Program
{
    /// <summary>
    /// Tells Serilog which assembly provides the sinks named in the configuration file.
    /// </summary>
    /// <remarks>
    /// By default Serilog discovers sinks by scanning the files next to the executable. That fails in a
    /// single-file publish, where the assemblies are embedded rather than present as separate files, and
    /// the application would abort at startup with "no Serilog assemblies were found". Naming the sink
    /// assembly explicitly through any of its types removes the need for discovery entirely.
    /// </remarks>
    private static readonly ConfigurationReaderOptions SerilogReaderOptions =
        new(typeof(FileLoggerConfigurationExtensions).Assembly);

    /// <summary>
    /// Sets up configuration and logging, then dispatches to the console or graphical interface.
    /// </summary>
    /// <remarks>
    /// Configuration is layered in order of increasing priority: the shipped <c>appsettings.json</c>,
    /// then environment variables prefixed with <c>BOOTMANAGER_</c>, then the command line. A setting
    /// specified later overrides the same setting specified earlier, so a value can always be
    /// overridden at launch without editing any file.
    /// <para>
    /// <c>[STAThread]</c> marks the main thread as a "single threaded apartment", which the Windows UI
    /// components require. It is harmless on the other platforms.
    /// </para>
    /// </remarks>
    /// <param name="args">The raw command line arguments.</param>
    /// <returns>The process exit code: 0 on success, non-zero when a console command failed.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        var isCommandLine = CommandLineInterface.IsCommandLineInvocation(args);

        // The configuration provider only understands switch syntax ("--key value", "key=value") and
        // throws on a bare word, so the command and its operands are withheld from it.
        var configurationArgs = isCommandLine ? args.Skip(1).Where(a => a.StartsWith('-')).ToArray() : args;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "BOOTMANAGER_")
            .AddCommandLine(configurationArgs)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration, SerilogReaderOptions)
            .Enrich.FromLogContext()
            .CreateLogger();

        App.Configuration = configuration;

        try
        {
            if (isCommandLine)
            {
                // Needed because this is a GUI executable and would otherwise have nowhere to print to.
                CommandLineInterface.AttachToParentConsole();

                // The UI framework is never started in this mode, so blocking here is safe.
                return CommandLineInterface.RunAsync(args).GetAwaiter().GetResult();
            }

            Log.Verbose("Starting BootManager application");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception during application startup");
            return 1;
        }
        finally
        {
            // Serilog buffers writes, so the log file would be incomplete without this.
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Builds the Avalonia application. Also called by the XAML designer, which is why it must stay
    /// a public method with this exact shape.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
