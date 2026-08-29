using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using BootManager.Models;
using Microsoft.Win32;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Collects boot diagnostics on Windows.
/// </summary>
/// <remarks>
/// <para>
/// Most facts are read from the registry rather than through WMI. The registry keys used here are
/// populated by Windows itself from the machine's SMBIOS tables, so they carry the same data, but
/// reading them is immediate and needs no extra dependency - WMI would require the Windows-only
/// <c>System.Management</c> package and a noticeably slower query.
/// </para>
/// <para>
/// The one exception is the duration of the last boot, which is only available in an event log record
/// and is therefore fetched through PowerShell.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemInfoService : ISystemInfoService
{
    /// <summary>Registry key holding the firmware and system identification copied from SMBIOS.</summary>
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";

    /// <summary>Registry key where the firmware reports the current Secure Boot state.</summary>
    private const string SecureBootKey = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";

    /// <summary>Registry key holding the power settings, including the Fast Startup flag.</summary>
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    /// <summary>Extracts the boot duration in milliseconds from the event record's XML payload.</summary>
    private static readonly Regex BootTimeRegex = new(@"<Data Name=['""]BootTime['""]>(?<ms>\d+)</Data>", RegexOptions.Compiled);

    /// <summary>
    /// The Win32 <c>GetFirmwareType</c> function, which reports how the machine was booted.
    /// </summary>
    /// <remarks>
    /// This is the authoritative answer to "is this machine running in UEFI mode", which matters
    /// because a machine installed in legacy BIOS mode has no UEFI boot entries at all.
    /// </remarks>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out uint firmwareType);

    /// <summary>Firmware type values returned by <see cref="GetFirmwareType"/>.</summary>
    private const uint FirmwareTypeBios = 1;
    private const uint FirmwareTypeUefi = 2;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemInfoItem>> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("Collecting Windows boot diagnostics");

        var items = new List<SystemInfoItem>(CommonSystemInfo.GetRuntimeFacts())
        {
            new(CommonSystemInfo.FirmwareCategory, "Boot mode", GetBootMode()),
            new(CommonSystemInfo.FirmwareCategory, "Secure Boot", GetSecureBootState()),
            new(CommonSystemInfo.FirmwareCategory, "Firmware vendor", ReadBiosValue("BIOSVendor")),
            new(CommonSystemInfo.FirmwareCategory, "Firmware version", ReadBiosValue("BIOSVersion")),
            new(CommonSystemInfo.FirmwareCategory, "Firmware date", ReadBiosValue("BIOSReleaseDate")),
            new(CommonSystemInfo.HardwareCategory, "Manufacturer", ReadBiosValue("SystemManufacturer")),
            new(CommonSystemInfo.HardwareCategory, "Model", ReadBiosValue("SystemProductName")),
            new(CommonSystemInfo.HardwareCategory, "Mainboard", $"{ReadBiosValue("BaseBoardManufacturer")} {ReadBiosValue("BaseBoardProduct")}".Trim()),
            new(CommonSystemInfo.BootTimingCategory, "Fast Startup (hiberboot)", GetFastStartupState()),
            new(CommonSystemInfo.BootTimingCategory, "Last boot duration", await GetLastBootDurationAsync(cancellationToken).ConfigureAwait(false)),
        };

        Log.Verbose("Collected {Count} Windows diagnostic values", items.Count);
        return items;
    }

    /// <summary>Reports whether the machine was started through UEFI or through a legacy BIOS.</summary>
    private static string GetBootMode()
    {
        if (!GetFirmwareType(out var firmwareType))
        {
            return CommonSystemInfo.Unknown;
        }

        return firmwareType switch
        {
            FirmwareTypeUefi => "UEFI",
            FirmwareTypeBios => "Legacy BIOS (CSM) - no UEFI boot entries available",
            _ => CommonSystemInfo.Unknown,
        };
    }

    /// <summary>
    /// Reports the Secure Boot state.
    /// </summary>
    /// <remarks>
    /// The value <c>UEFISecureBootEnabled</c> is 1 when Secure Boot is active. The key is missing
    /// entirely on legacy BIOS machines, which is reported as "not supported" rather than "disabled",
    /// because the two mean different things when diagnosing a rejected boot entry.
    /// </remarks>
    private static string GetSecureBootState()
    {
        var value = ReadLocalMachineValue(SecureBootKey, "UEFISecureBootEnabled");
        return value switch
        {
            1 => "Enabled (unsigned boot loaders will be rejected)",
            0 => "Disabled",
            _ => "Not supported on this system",
        };
    }

    /// <summary>
    /// Reports whether Windows Fast Startup is enabled.
    /// </summary>
    /// <remarks>
    /// Fast Startup ("hiberboot") does not really shut the machine down; it hibernates the kernel. That
    /// leaves file systems in a state other operating systems must not write to, so it is one of the
    /// most common causes of dual-boot trouble - hence its prominence in these diagnostics.
    /// </remarks>
    private static string GetFastStartupState()
    {
        var value = ReadLocalMachineValue(PowerKey, "HiberbootEnabled");
        return value switch
        {
            1 => "Enabled - shutdown hibernates the kernel, which can corrupt or lock disks shared with another OS",
            0 => "Disabled",
            _ => CommonSystemInfo.Unknown,
        };
    }

    /// <summary>
    /// Reads how long the last boot took.
    /// </summary>
    /// <remarks>
    /// Windows records this in the "Diagnostics-Performance" event log, event id 100, whose payload
    /// contains a <c>BootTime</c> value in milliseconds. There is no cross-platform .NET API for that
    /// log, so PowerShell is used to query it. The log is only readable by administrators, and it does
    /// not exist on Windows Server, so a failure here is reported as a message rather than thrown.
    /// </remarks>
    private static async Task<string> GetLastBootDurationAsync(CancellationToken cancellationToken)
    {
        // powershell -NoProfile -NonInteractive -Command "..."
        //   -NoProfile        skips the user's profile scripts, which makes startup faster and avoids
        //                     any side effects from a customised environment.
        //   -NonInteractive   guarantees the command never stops to ask the user something.
        //   -Command          the script to run:
        //                       Get-WinEvent reads the boot performance log, -MaxEvents 1 takes only
        //                       the newest record, and .ToXml() exposes the payload fields, which the
        //                       object model does not surface directly.
        const string script =
            "(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Diagnostics-Performance/Operational';Id=100} -MaxEvents 1).ToXml()";

        var result = await ProcessRunner
            .RunAsync("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"", cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return "Unavailable (the boot performance log could not be read)";
        }

        var match = BootTimeRegex.Match(result.StandardOutput);
        if (!match.Success)
        {
            return "Unavailable (no boot performance record found)";
        }

        var duration = TimeSpan.FromMilliseconds(double.Parse(match.Groups["ms"].Value));
        return CommonSystemInfo.FormatDuration(duration);
    }

    /// <summary>Reads a string from the SMBIOS-derived registry key, or "Unknown" if it is absent.</summary>
    private static string ReadBiosValue(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(BiosKey);
        return key?.GetValue(name) as string is { Length: > 0 } value ? value : CommonSystemInfo.Unknown;
    }

    /// <summary>Reads a numeric registry value, returning <see langword="null"/> when key or value is missing.</summary>
    private static int? ReadLocalMachineValue(string keyPath, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValue(name) as int?;
    }
}
