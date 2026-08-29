using System.Runtime.InteropServices;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// The non-interactive command line interface of the application.
/// </summary>
/// <remarks>
/// <para>
/// The same executable serves both as a desktop application and as a console tool. When the first
/// argument names one of the commands below, the graphical interface is never created and the program
/// prints its result to the console instead, which makes it usable from scripts.
/// </para>
/// <para>
/// <b>Elevation behaves differently here than in the GUI.</b> The window offers to restart itself with
/// elevated privileges, but a command line invocation must not: relaunching would create a new process
/// that is detached from the caller's console, so the output would vanish and the exit code would be
/// meaningless to whatever script invoked it. The CLI therefore just reports the problem and fails.
/// </para>
/// </remarks>
public static class CommandLineInterface
{
    /// <summary>Exit code reported when a command completed successfully.</summary>
    private const int ExitSuccess = 0;

    /// <summary>Exit code reported when a command failed for any reason.</summary>
    private const int ExitFailure = 1;

    /// <summary>The commands recognised as the first argument, compared case-insensitively.</summary>
    private static readonly string[] KnownCommands = ["list", "setnext", "setdef", "bootuefi", "info", "help", "--help", "-h"];

    /// <summary>
    /// Decides whether the given command line asks for console mode rather than the window.
    /// </summary>
    /// <remarks>
    /// Only the first argument is examined. Anything else - including no arguments at all, or only
    /// configuration overrides such as <c>--Serilog:MinimumLevel=Debug</c> - starts the GUI.
    /// </remarks>
    public static bool IsCommandLineInvocation(string[] args) =>
        args.Length > 0 && KnownCommands.Contains(args[0], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Executes the requested command and prints the result.
    /// </summary>
    /// <param name="args">The raw command line arguments; the first one selects the command.</param>
    /// <returns>0 when the command succeeded, 1 when it failed. This becomes the process exit code.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args[0].ToLowerInvariant();
        Log.Verbose("Running command line command {Command}", command);

        if (command is "help" or "--help" or "-h")
        {
            PrintUsage();
            return ExitSuccess;
        }

        // Every remaining command touches firmware state, so refuse early with a clear message rather
        // than letting the underlying tool fail with a less obvious "access denied".
        if (!ElevationService.IsElevated())
        {
            Console.Error.WriteLine(
                $"Error: this command requires {ElevationService.RequiredPrivilegeName} privileges. "
                + "Re-run it from an elevated console.");
            Log.Error("Command {Command} refused: missing {Privilege} privileges", command, ElevationService.RequiredPrivilegeName);
            return ExitFailure;
        }

        try
        {
            return command switch
            {
                "list" => await ListAsync(),
                "setnext" => await SetBootEntryAsync(args, next: true),
                "setdef" => await SetBootEntryAsync(args, next: false),
                "bootuefi" => await BootToFirmwareSetupAsync(),
                "info" => await PrintSystemInfoAsync(),
                _ => Unreachable(),
            };
        }
        catch (Exception ex)
        {
            // The console equivalent of the window's notification banner: report and fail, never crash.
            Console.Error.WriteLine($"Error: {ex.Message}");
            Log.Error(ex, "Command {Command} failed", command);
            return ExitFailure;
        }
    }

    /// <summary>Prints all boot entries, marking the default and the next boot entry.</summary>
    private static async Task<int> ListAsync()
    {
        var entries = await BootManagerServiceFactory.Create().GetBootEntriesAsync();
        if (entries.Count == 0)
        {
            Console.WriteLine("No boot entries found.");
            return ExitSuccess;
        }

        // Width the id column to the longest id so the output stays aligned on every platform,
        // since ids range from four characters on Linux to full GUIDs on Windows.
        var idWidth = Math.Max(2, entries.Max(e => e.Id.Length));
        Console.WriteLine($"{"ID".PadRight(idWidth)}  FLAGS  DESCRIPTION");

        foreach (var entry in entries)
        {
            // "*" marks the persistent default, ">" the entry that will actually boot next.
            var flags = $"{(entry.IsCurrentDefault ? "*" : " ")}{(entry.IsNextBoot ? ">" : " ")}";
            Console.WriteLine($"{entry.Id.PadRight(idWidth)}  {flags,-5}  {entry.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("* = default (every boot)   > = next boot (one time)");
        return ExitSuccess;
    }

    /// <summary>
    /// Applies either the one-time or the persistent boot selection.
    /// </summary>
    /// <param name="args">Command line arguments; the second one must be the entry id.</param>
    /// <param name="next">
    /// <see langword="true"/> for a one-time override, <see langword="false"/> to change the default.
    /// </param>
    private static async Task<int> SetBootEntryAsync(string[] args, bool next)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"Error: '{args[0]}' requires a boot entry id. Run 'list' to see the available ids.");
            return ExitFailure;
        }

        var id = args[1];
        var service = BootManagerServiceFactory.Create();

        // The id is resolved against the real entries instead of being passed through blindly: this
        // catches typos before anything is written, and yields the description for the confirmation.
        var entries = await service.GetBootEntriesAsync();
        var entry = entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            Console.Error.WriteLine($"Error: no boot entry with id '{id}'. Run 'list' to see the available ids.");
            return ExitFailure;
        }

        if (next)
        {
            await service.SetNextBootEntryAsync(entry);
            Console.WriteLine($"Next boot set to '{entry.Description}' ({entry.Id}). This applies once; the default is unchanged.");
        }
        else
        {
            await service.SetDefaultBootEntryAsync(entry);
            Console.WriteLine($"Default boot entry set to '{entry.Description}' ({entry.Id}).");
        }

        return ExitSuccess;
    }

    /// <summary>Arms the request to show the firmware setup screen on the next boot.</summary>
    private static async Task<int> BootToFirmwareSetupAsync()
    {
        await BootManagerServiceFactory.Create().RequestBootToFirmwareSetupAsync();
        Console.WriteLine("The system will open the UEFI firmware setup on the next boot.");
        return ExitSuccess;
    }

    /// <summary>Prints the boot diagnostics, grouped by category.</summary>
    private static async Task<int> PrintSystemInfoAsync()
    {
        var info = await BootManagerServiceFactory.CreateSystemInfoService().GetSystemInfoAsync();

        foreach (var group in info.GroupBy(i => i.Category))
        {
            Console.WriteLine($"[{group.Key}]");

            var labelWidth = group.Max(i => i.Label.Length);
            foreach (var item in group)
            {
                Console.WriteLine($"  {item.Label.PadRight(labelWidth)}  {item.Value}");
            }

            Console.WriteLine();
        }

        return ExitSuccess;
    }

    /// <summary>Prints the list of commands and their meaning.</summary>
    private static void PrintUsage()
    {
        Console.WriteLine("BootManager - inspect and change UEFI boot options.");
        Console.WriteLine();
        Console.WriteLine("Usage: BootManager [command] [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  list             List the available boot entries with their ids.");
        Console.WriteLine("  setnext <id>     Boot the given entry on the next start only, then revert to the default.");
        Console.WriteLine("  setdef <id>      Make the given entry the permanent default.");
        Console.WriteLine("  bootUEFI         Open the UEFI firmware setup on the next boot.");
        Console.WriteLine("  info             Print boot related system information.");
        Console.WriteLine("  help             Show this text.");
        Console.WriteLine();
        Console.WriteLine("Start without a command to open the graphical interface.");
        Console.WriteLine($"All commands except 'help' require {ElevationService.RequiredPrivilegeName} privileges.");
    }

    /// <summary>Guards the switch expression; <see cref="IsCommandLineInvocation"/> already filtered the input.</summary>
    private static int Unreachable() => throw new InvalidOperationException("Unrecognised command.");

    /// <summary>
    /// Attaches the process to the console of whoever started it, on Windows only.
    /// </summary>
    /// <remarks>
    /// The application is built as a Windows GUI executable so that no console window flashes up when
    /// it is started from Explorer. The side effect is that it has no console of its own, and anything
    /// written to <see cref="Console"/> would be discarded when run from a command prompt. Attaching to
    /// the parent's console restores the expected behaviour for the CLI. On Linux and macOS the
    /// standard streams are inherited normally, so nothing needs to be done there.
    /// </remarks>
    public static void AttachToParentConsole()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // A failure here is not worth reporting: it simply means there was no console to attach to,
        // for instance when the command was started from a shortcut.
        AttachConsole(AttachParentProcess);
    }

    /// <summary>Value of <c>ATTACH_PARENT_PROCESS</c>: use the console of the parent process.</summary>
    private const int AttachParentProcess = -1;

    /// <summary>Attaches the calling process to an existing console.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
