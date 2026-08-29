using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Serilog;

namespace BootManager.Services;

/// <summary>Detects whether the process has administrator/root privileges and can relaunch itself elevated.</summary>
public static class ElevationService
{
    [DllImport("libc", EntryPoint = "geteuid", SetLastError = true)]
    private static extern uint GetEffectiveUserId();

    public static bool IsElevated()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetEffectiveUserId() == 0;
        }

        return false;
    }

    public static string RequiredPrivilegeName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Administrator" : "root (sudo)";

    public static string RestartActionLabel =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Restart as Administrator" : "Restart with sudo";

    /// <summary>Relaunches the current executable elevated and terminates this process.</summary>
    public static void RestartElevated()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current executable path.");
        var originalArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

        Log.Verbose("Restarting process elevated: {ExePath} {Args}", exePath, string.Join(' ', originalArgs));

        var startInfo = BuildElevatedStartInfo(exePath, originalArgs);
        Process.Start(startInfo);
        Environment.Exit(0);
    }

    private static ProcessStartInfo BuildElevatedStartInfo(string exePath, string[] originalArgs)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var startInfo = new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" };
            foreach (var arg in originalArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return startInfo;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // pkexec shows a native polkit prompt, unlike sudo it does not require a TTY.
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
            var shellCommand = string.Join(' ', new[] { exePath }.Concat(originalArgs).Select(ShellQuote));
            var appleScript = $"do shell script \"{shellCommand.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" with administrator privileges";
            var startInfo = new ProcessStartInfo("osascript") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(appleScript);
            return startInfo;
        }

        throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    private static string ShellQuote(string arg) => "'" + arg.Replace("'", "'\\''") + "'";
}
