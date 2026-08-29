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
    /// line survives the restart. This method does not return on success: the current process exits as
    /// soon as the elevated one has been launched.
    /// <para>
    /// On Windows, <c>Process.Start</c> with the "runas" verb itself blocks until the UAC prompt is
    /// answered, and throws if the user declines - so reaching the exit call already proves elevation
    /// worked. <c>pkexec</c> and <c>osascript</c> behave differently: they return only once their entire
    /// child process tree has exited, so - unlike UAC - <c>Process.Start</c> returning is not proof that
    /// authentication even happened yet, let alone succeeded. Both helpers are therefore invoked through
    /// a shell wrapper that backgrounds the real executable and returns immediately once authentication
    /// completes, and this method waits for that helper to exit and inspects its exit code before
    /// deciding whether elevation actually succeeded.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The path of the running executable could not be determined, the helper process could not be
    /// started, or the helper reported that elevation was not granted (e.g. the password was rejected or
    /// the user cancelled the prompt).
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">There is no known way to elevate on this platform.</exception>
    public static void RestartElevated()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current executable path.");

        // Skip(1) drops the executable path itself, which is the first element of the command line.
        var originalArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

        Log.Verbose("Restarting process elevated: {ExePath} {Args}", exePath, string.Join(' ', originalArgs));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(BuildWindowsElevatedStartInfo(exePath, originalArgs));
            Environment.Exit(0);
            return;
        }

        var startInfo = BuildDetachedElevationStartInfo(exePath, originalArgs);
        startInfo.RedirectStandardError = true;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the elevation helper process.");

        // Read before WaitForExit: the pipe buffer is small enough that a helper writing a long error
        // could otherwise deadlock waiting for us to drain it while we wait for it to exit.
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? string.Empty : $": {stderr.Trim()}";
            throw new InvalidOperationException(
                $"Elevation was not granted (exit code {process.ExitCode}){detail}. "
                + "This usually means the password was rejected, the prompt was cancelled, or this user "
                + "account is not configured as an administrator for polkit/sudo.");
        }

        Environment.Exit(0);
    }

    /// <summary>Builds the Windows-specific command that relaunches the application elevated via UAC.</summary>
    private static ProcessStartInfo BuildWindowsElevatedStartInfo(string exePath, string[] originalArgs)
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

    /// <summary>
    /// Builds the Linux/macOS command that authenticates and then launches the application elevated,
    /// detached from the authentication helper so the helper's exit code reports only whether
    /// authentication succeeded.
    /// </summary>
    /// <remarks>
    /// Each platform has its own authentication mechanism, and each shows the user a native prompt:
    /// <list type="bullet">
    ///   <item><description>Linux: <c>pkexec</c>, which shows a graphical polkit prompt.</description></item>
    ///   <item><description>macOS: AppleScript, which shows the standard password dialog.</description></item>
    /// </list>
    /// Both normally wait for the elevated program to exit before returning, which would hang this
    /// method for as long as the elevated window stays open. The inner shell command therefore starts
    /// the real executable detached (backgrounded and reparented away from the helper, stdio closed) and
    /// returns immediately, so the helper itself only waits for authentication to complete.
    /// </remarks>
    private static ProcessStartInfo BuildDetachedElevationStartInfo(string exePath, string[] originalArgs)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // pkexec sh -c 'setsid "$@" </dev/null >/dev/null 2>&1 &' sh <exe> [args...]
            //   setsid    starts the real executable in a new session so it is not a child that pkexec
            //             is waiting on.
            //   "$@" &    backgrounds the real executable so the wrapping "sh -c" returns immediately
            //             instead of waiting for it to exit.
            //   redirects detach stdio so pkexec has no open pipe to the elevated app to wait on.
            // The leading "sh" argument fills $0 inside the script; "$@" then expands to <exe> [args...].
            var startInfo = new ProcessStartInfo("pkexec") { UseShellExecute = false };
            startInfo.ArgumentList.Add("sh");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("setsid \"$@\" </dev/null >/dev/null 2>&1 &");
            startInfo.ArgumentList.Add("sh");
            startInfo.ArgumentList.Add(exePath);
            foreach (var arg in originalArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return startInfo;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // osascript -e 'do shell script "nohup ... </dev/null >/dev/null 2>&1 &" with administrator privileges'
            //   -e <script>                     runs the given AppleScript directly.
            //   with administrator privileges   makes macOS show its native password dialog and run
            //                                   the command as root.
            //   nohup ... &                     backgrounds the real executable (macOS has no setsid),
            //                                   so the shell command returns as soon as authentication
            //                                   succeeds instead of waiting for the app to close.
            // The command is a single string interpreted by a shell, so each part is quoted for the
            // shell first and the result is then escaped again for the AppleScript string literal.
            var shellCommand = "nohup " + string.Join(' ', new[] { exePath }.Concat(originalArgs).Select(ShellQuote))
                + " </dev/null >/dev/null 2>&1 &";
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
