using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Detects whether the application has the elevated privileges it needs, and can relaunch itself
/// elevated when it does not.
/// </summary>
/// <remarks>
/// Every boot manager operation touches firmware variables, which no operating system allows to
/// unprivileged users. Rather than failing with a confusing "access denied" from a system tool, the
/// app checks up front and offers to restart itself properly.
/// <para>
/// A process cannot gain privileges while it is running, so "elevating" always means starting a second
/// process through the platform's own authentication mechanism and ending the current one.
/// </para>
/// </remarks>
public static class ElevationService
{
    /// <summary>
    /// The POSIX <c>geteuid</c> function, which returns the effective user id of the process.
    /// An id of 0 means root. Declared for libc, which exists on both Linux and macOS.
    /// </summary>
    [DllImport("libc", EntryPoint = "geteuid", SetLastError = true)]
    private static extern uint GetEffectiveUserId();

    /// <summary>
    /// Checks whether the process currently has the privileges required to change boot settings.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when running as Administrator (Windows) or root (Linux/macOS);
    /// <see langword="false"/> otherwise, including on unknown platforms, where assuming the worst is
    /// safer than promising privileges that may not exist.
    /// </returns>
    public static bool IsElevated()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows models privileges as group membership: an elevated process runs with the
            // Administrators group enabled in its token, an unelevated one does not - even for a user
            // who is a member of that group.
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetEffectiveUserId() == 0;
        }

        return false;
    }

    /// <summary>The platform's name for the required privilege, for use in messages shown to the user.</summary>
    public static string RequiredPrivilegeName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Administrator" : "root (sudo)";

    /// <summary>The caption for the button that restarts the application elevated.</summary>
    public static string RestartActionLabel =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Restart as Administrator" : "Restart with sudo";

    /// <summary>
    /// Starts a new elevated instance of this application and terminates the current one.
    /// </summary>
    /// <remarks>
    /// The original command line arguments are forwarded so that configuration passed on the command
    /// line survives the restart. This method does not return: the current process exits as soon as the
    /// elevated one has been launched. If the user declines the authentication prompt, the new process
    /// never starts and the platform raises an exception, which the caller reports.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The path of the running executable could not be determined.</exception>
    /// <exception cref="PlatformNotSupportedException">There is no known way to elevate on this platform.</exception>
    public static void RestartElevated()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current executable path.");

        // Skip(1) drops the executable path itself, which is the first element of the command line.
        var originalArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

        Log.Verbose("Restarting process elevated: {ExePath} {Args}", exePath, string.Join(' ', originalArgs));

        var startInfo = BuildElevatedStartInfo(exePath, originalArgs);
        Process.Start(startInfo);
        Environment.Exit(0);
    }

    /// <summary>
    /// Builds the platform specific command that launches the application with elevated privileges.
    /// </summary>
    /// <remarks>
    /// Each platform has its own mechanism, and each shows the user a native authentication prompt:
    /// <list type="bullet">
    ///   <item><description>Windows: the "runas" shell verb, which triggers the UAC consent dialog.</description></item>
    ///   <item><description>Linux: <c>pkexec</c>, which shows a graphical polkit prompt.</description></item>
    ///   <item><description>macOS: AppleScript, which shows the standard password dialog.</description></item>
    /// </list>
    /// </remarks>
    private static ProcessStartInfo BuildElevatedStartInfo(string exePath, string[] originalArgs)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // The "runas" verb is only honoured by the shell, so UseShellExecute must be true here -
            // starting the process directly would silently launch it unelevated again.
            var startInfo = new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" };

            // ArgumentList quotes each argument correctly, e.g. paths containing spaces.
            foreach (var arg in originalArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return startInfo;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // pkexec <program> [args...]
            //   Runs the program as root after asking for authentication through polkit. Chosen over
            //   sudo because sudo expects a terminal to read the password from, which a GUI app has not.
            var startInfo = new ProcessStartInfo("pkexec") { UseShellExecute = false };
            startInfo.ArgumentList.Add(exePath);
            foreach (var arg in originalArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return startInfo;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // osascript -e 'do shell script "..." with administrator privileges'
            //   -e <script>                     runs the given AppleScript directly.
            //   with administrator privileges   makes macOS show its native password dialog and run
            //                                   the command as root.
            // The command is a single string interpreted by a shell, so each part is quoted for the
            // shell first and the result is then escaped again for the AppleScript string literal.
            var shellCommand = string.Join(' ', new[] { exePath }.Concat(originalArgs).Select(ShellQuote));
            var appleScript = $"do shell script \"{shellCommand.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" with administrator privileges";
            var startInfo = new ProcessStartInfo("osascript") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(appleScript);
            return startInfo;
        }

        throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// Wraps a value in single quotes for POSIX shells, so spaces and special characters are taken
    /// literally. An embedded single quote is handled with the usual <c>'\''</c> idiom: close the
    /// quoted section, emit an escaped quote, reopen it.
    /// </summary>
    private static string ShellQuote(string arg) => "'" + arg.Replace("'", "'\\''") + "'";
}
