using System.Runtime.InteropServices;
using BootManager.Models;

namespace BootManager.Services;

/// <summary>
/// Facts that can be determined the same way on every operating system, plus small formatting helpers
/// shared by the platform specific implementations.
/// </summary>
internal static class CommonSystemInfo
{
    /// <summary>Category name used for the operating system section.</summary>
    internal const string OperatingSystemCategory = "Operating system";

    /// <summary>Category name used for the firmware section.</summary>
    internal const string FirmwareCategory = "Firmware";

    /// <summary>Category name used for the hardware section.</summary>
    internal const string HardwareCategory = "Hardware";

    /// <summary>Category name used for boot times and durations.</summary>
    internal const string BootTimingCategory = "Boot timing";

    /// <summary>Shown instead of a value that could not be determined.</summary>
    internal const string Unknown = "Unknown";

    /// <summary>
    /// Returns the facts that come straight from the .NET runtime and therefore need no platform code.
    /// </summary>
    /// <remarks>
    /// The boot time is derived from the uptime rather than read from the system, because that works
    /// identically everywhere. Note that on Windows this reflects the time since the kernel started,
    /// which with Fast Startup enabled can be much older than the user's last "shutdown".
    /// </remarks>
    internal static IEnumerable<SystemInfoItem> GetRuntimeFacts()
    {
        yield return new SystemInfoItem(OperatingSystemCategory, "Operating system", RuntimeInformation.OSDescription);
        yield return new SystemInfoItem(OperatingSystemCategory, "Architecture", RuntimeInformation.OSArchitecture.ToString());
        yield return new SystemInfoItem(OperatingSystemCategory, "Machine name", Environment.MachineName);

        // TickCount64 counts milliseconds since the system started and, unlike the 32 bit version,
        // does not wrap around after 49 days.
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        yield return new SystemInfoItem(BootTimingCategory, "System started", (DateTime.Now - uptime).ToString("yyyy-MM-dd HH:mm:ss"));
        yield return new SystemInfoItem(BootTimingCategory, "Uptime", FormatDuration(uptime));
    }

    /// <summary>Formats a duration compactly, omitting the parts that are zero.</summary>
    internal static string FormatDuration(TimeSpan value) => value switch
    {
        { TotalDays: >= 1 } => $"{(int)value.TotalDays} d {value.Hours} h {value.Minutes} min",
        { TotalHours: >= 1 } => $"{(int)value.TotalHours} h {value.Minutes} min",
        { TotalMinutes: >= 1 } => $"{(int)value.TotalMinutes} min {value.Seconds} s",
        _ => $"{value.TotalSeconds:0.#} s",
    };

    /// <summary>Renders a nullable boolean as an explicit yes/no, or as "unknown" when it was not readable.</summary>
    internal static string FormatEnabled(bool? value) => value switch
    {
        true => "Enabled",
        false => "Disabled",
        null => Unknown,
    };
}
