using System.Runtime.InteropServices;

namespace BootManager.Services;

/// <summary>
/// Picks the <see cref="IBootManagerService"/> implementation that matches the operating system the
/// application is currently running on.
/// </summary>
/// <remarks>
/// The three implementations have nothing in common beyond the interface - they drive completely
/// different tools - so the choice is made once here at startup rather than being scattered through
/// the code with platform checks. The check happens at run time, not compile time, because a single
/// build of this application is meant to run on all three platforms.
/// </remarks>
public static class BootManagerServiceFactory
{
    /// <summary>
    /// Creates the boot manager implementation for the current operating system.
    /// </summary>
    /// <returns>A Windows, Linux or macOS specific implementation.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// The application is running on an operating system for which no implementation exists.
    /// </exception>
    public static IBootManagerService Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsBootManagerService();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxBootManagerService();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacBootManagerService();
        }

        throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// Creates the diagnostics collector for the current operating system.
    /// </summary>
    /// <returns>A Windows, Linux or macOS specific implementation.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// The application is running on an operating system for which no implementation exists.
    /// </exception>
    public static ISystemInfoService CreateSystemInfoService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsSystemInfoService();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxSystemInfoService();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacSystemInfoService();
        }

        throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
    }
}
