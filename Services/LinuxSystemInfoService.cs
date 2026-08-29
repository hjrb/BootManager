using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Collects boot diagnostics on Linux.
/// </summary>
/// <remarks>
/// Linux exposes almost everything needed here through virtual file systems, which can simply be read
/// as text files and require no privileges:
/// <list type="bullet">
///   <item><description>
///     <c>/sys/firmware/efi</c> only exists when the machine was booted through UEFI. Its presence is
///     the standard test for the boot mode.
///   </description></item>
///   <item><description>
///     <c>/sys/class/dmi/id/*</c> mirrors the SMBIOS tables, giving firmware vendor, version and date
///     as well as the machine and mainboard identification.
///   </description></item>
///   <item><description>
///     <c>/proc/uptime</c> holds the uptime in seconds.
///   </description></item>
/// </list>
/// Only Secure Boot and the boot duration need external tools.
/// </remarks>
public sealed class LinuxSystemInfoService : ISystemInfoService
{
    /// <summary>Directory that the kernel only creates when running on UEFI firmware.</summary>
    private const string EfiDirectory = "/sys/firmware/efi";

    /// <summary>Directory exposing the SMBIOS/DMI identification strings as individual files.</summary>
    private const string DmiDirectory = "/sys/class/dmi/id";

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemInfoItem>> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("Collecting Linux boot diagnostics");

        var isEfi = Directory.Exists(EfiDirectory);

        var items = new List<SystemInfoItem>(CommonSystemInfo.GetRuntimeFacts())
        {
            new(CommonSystemInfo.FirmwareCategory, "Boot mode", isEfi ? "UEFI" : "Legacy BIOS (CSM) - no UEFI boot entries available"),
            new(CommonSystemInfo.FirmwareCategory, "UEFI bitness", GetEfiPlatformSize(isEfi)),
            new(CommonSystemInfo.FirmwareCategory, "Secure Boot", await GetSecureBootStateAsync(cancellationToken).ConfigureAwait(false)),
            new(CommonSystemInfo.FirmwareCategory, "Firmware vendor", ReadDmi("bios_vendor")),
            new(CommonSystemInfo.FirmwareCategory, "Firmware version", ReadDmi("bios_version")),
            new(CommonSystemInfo.FirmwareCategory, "Firmware date", ReadDmi("bios_date")),
            new(CommonSystemInfo.HardwareCategory, "Manufacturer", ReadDmi("sys_vendor")),
            new(CommonSystemInfo.HardwareCategory, "Model", ReadDmi("product_name")),
            new(CommonSystemInfo.HardwareCategory, "Mainboard", $"{ReadDmi("board_vendor")} {ReadDmi("board_name")}".Trim()),
            new(CommonSystemInfo.BootTimingCategory, "Last boot duration", await GetBootDurationAsync(cancellationToken).ConfigureAwait(false)),
        };

        Log.Verbose("Collected {Count} Linux diagnostic values", items.Count);
        return items;
    }

    /// <summary>
    /// Reports whether the firmware runs in 32 or 64 bit mode.
    /// </summary>
    /// <remarks>
    /// Relevant when a boot loader refuses to load: a 64 bit loader cannot be started by 32 bit
    /// firmware, a mismatch found on some older tablets.
    /// </remarks>
    private static string GetEfiPlatformSize(bool isEfi)
    {
        if (!isEfi)
        {
            return "Not applicable";
        }

        var value = ReadFileOrNull($"{EfiDirectory}/fw_platform_size");
        return value is null ? CommonSystemInfo.Unknown : $"{value}-bit";
    }

    /// <summary>
    /// Reads the Secure Boot state using <c>mokutil</c>.
    /// </summary>
    /// <remarks>
    /// <c>mokutil</c> is part of the shim boot loader package and is not installed everywhere, so its
    /// absence is reported as an explanation rather than treated as an error.
    /// </remarks>
    private static async Task<string> GetSecureBootStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            // mokutil --sb-state
            //   --sb-state   prints "SecureBoot enabled" or "SecureBoot disabled".
            var result = await ProcessRunner.RunAsync("mokutil", "--sb-state", cancellationToken).ConfigureAwait(false);
            var output = (result.StandardOutput + result.StandardError).Trim();

            if (output.Contains("enabled", StringComparison.OrdinalIgnoreCase))
            {
                return "Enabled (unsigned boot loaders will be rejected)";
            }

            return output.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                ? "Disabled"
                : CommonSystemInfo.Unknown;
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "mokutil is not available, Secure Boot state cannot be determined");
            return "Unknown (mokutil is not installed)";
        }
    }

    /// <summary>
    /// Reads how long the last boot took, broken down by phase.
    /// </summary>
    /// <remarks>
    /// <c>systemd-analyze</c> prints a line such as
    /// <c>Startup finished in 4.2s (firmware) + 3.1s (loader) + 1.9s (kernel) + 12.4s (userspace) = 21.7s</c>.
    /// That breakdown is exactly what is needed to tell a slow firmware apart from a slow service, so
    /// the line is passed through unchanged rather than reduced to a single number.
    /// </remarks>
    private static async Task<string> GetBootDurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("systemd-analyze", string.Empty, cancellationToken).ConfigureAwait(false);
            var output = result.StandardOutput.Trim();
            return result.Succeeded && output.Length > 0 ? output : CommonSystemInfo.Unknown;
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "systemd-analyze is not available, boot duration cannot be determined");
            return "Unknown (systemd-analyze is not available)";
        }
    }

    /// <summary>Reads one DMI identification string, e.g. "bios_vendor".</summary>
    private static string ReadDmi(string name) => ReadFileOrNull($"{DmiDirectory}/{name}") ?? CommonSystemInfo.Unknown;

    /// <summary>
    /// Reads a single line pseudo file.
    /// </summary>
    /// <returns>
    /// The trimmed contents, or <see langword="null"/> when the file is missing or unreadable. Missing
    /// files are normal here - not every machine populates every DMI field - so this never throws.
    /// </returns>
    private static string? ReadFileOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "Could not read {Path}", path);
            return null;
        }
    }
}
