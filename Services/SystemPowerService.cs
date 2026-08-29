using System.Runtime.InteropServices;

namespace BootManager.Services;

/// <summary>
/// Cross-platform actions that can be triggered from the main window's power menu.
/// </summary>
/// <remarks>
/// The menu intentionally only exposes actions that the host operating system supports. A forced
/// shutdown exists on Windows, but not in a portable cross-platform form, so Linux and macOS hide it
/// even though they support a normal shutdown.
/// </remarks>
public enum PowerActionKind
{
    ImmediateReboot,
    DelayedReboot,
    Shutdown,
    FullShutdown,
}

/// <summary>
/// Executes the platform-specific reboot and shutdown actions used by the application.
/// </summary>
public static class SystemPowerService
{
    /// <summary>
    /// Returns the supported actions for the current OS, in the order they should be shown in the UI.
    /// </summary>
    public static IReadOnlyList<PowerActionKind> GetSupportedActions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return [
                PowerActionKind.ImmediateReboot,
                PowerActionKind.DelayedReboot,
                PowerActionKind.Shutdown,
                PowerActionKind.FullShutdown,
            ];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return [
                PowerActionKind.ImmediateReboot,
                PowerActionKind.DelayedReboot,
                PowerActionKind.Shutdown,
            ];
        }

        return [];
    }

    /// <summary>
    /// Returns the UI label for a given power action.
    /// </summary>
    public static string GetLabel(PowerActionKind action) => action switch
    {
        PowerActionKind.ImmediateReboot => "Immediate reboot",
        PowerActionKind.DelayedReboot => "Delayed reboot (20s)",
        PowerActionKind.Shutdown => "Shutdown",
        PowerActionKind.FullShutdown => "Full shutdown",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>
    /// Triggers a reboot or shutdown action on the machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delayed reboot action is performed in the UI with a 20 second countdown window and then
    /// calls this method with <see cref="PowerActionKind.ImmediateReboot"/>. The platform-specific
    /// implementations below therefore handle the actual OS shutdown commands, not the countdown logic.
    /// </para>
    /// <para>
    /// The Windows "full shutdown" action is the closest equivalent to the request for a true full
    /// power-off rather than the hybrid shutdown that modern Windows can do. Linux and macOS expose no
    /// separate full-shutdown mode that is portable, so the action is hidden there.
    /// </para>
    /// </remarks>
    public static async Task ExecuteAsync(PowerActionKind action, CancellationToken cancellationToken = default)
    {
        switch (action)
        {
            case PowerActionKind.DelayedReboot:
                throw new NotSupportedException("Delayed reboot is handled by the countdown dialog and should not be invoked directly.");

            case PowerActionKind.ImmediateReboot:
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await RunAsync("shutdown.exe", "/r /t 0 /f", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    await RunAsync("systemctl", "reboot --no-wall", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    await RunAsync("shutdown", "-r now", cancellationToken).ConfigureAwait(false);
                    return;
                }

                throw new PlatformNotSupportedException($"Power actions are not supported on {RuntimeInformation.OSDescription}.");

            case PowerActionKind.Shutdown:
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await RunAsync("shutdown.exe", "/s /t 0 /f", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    await RunAsync("systemctl", "poweroff", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    await RunAsync("shutdown", "-h now", cancellationToken).ConfigureAwait(false);
                    return;
                }

                throw new PlatformNotSupportedException($"Power actions are not supported on {RuntimeInformation.OSDescription}.");

            case PowerActionKind.FullShutdown:
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    throw new NotSupportedException("This operating system does not support a separate full shutdown action.");
                }

                await RunAsync("shutdown.exe", "/s /f /t 0", cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private static async Task RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{fileName} failed with exit code {result.ExitCode}: {result.StandardError}");
        }
    }
}
