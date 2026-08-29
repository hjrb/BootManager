using System.Text.RegularExpressions;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// macOS implementation backed by diskutil (enumeration) and bless (selecting the startup volume).
/// Apple's firmware has no scriptable equivalent of a "one-time next boot" NVRAM entry or a UEFI
/// setup screen, so boot-option changes made here are persistent (IsNextBoot mirrors IsCurrentDefault)
/// and requesting firmware setup is not supported - the user must hold Option (Intel) or the power
/// button (Apple Silicon) during startup instead.
/// </summary>
public sealed partial class MacBootManagerService : IBootManagerService
{
    private static readonly Regex VolumeLineRegex = new(
        @"^\s*\d+:\s+\S+\s+(?<name>.+?)\s{2,}[\d.]+\s+\w{2}\s+(?<id>disk\S+)\s*$",
        RegexOptions.Compiled);

    public async Task<IReadOnlyList<BootEntry>> GetBootEntriesAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("Enumerating startup volumes via diskutil list");
        var listResult = await ProcessRunner.RunAsync("diskutil", "list", cancellationToken).ConfigureAwait(false);
        if (!listResult.Succeeded)
        {
            throw new InvalidOperationException($"diskutil failed with exit code {listResult.ExitCode}: {listResult.StandardError}");
        }

        Log.Verbose("Reading current boot device via bless --getBoot");
        var currentBootId = await GetCurrentBootDiskIdAsync(cancellationToken).ConfigureAwait(false);

        var entries = new List<BootEntry>();
        foreach (var line in listResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = VolumeLineRegex.Match(line.TrimEnd('\r'));
            if (!match.Success)
            {
                continue;
            }

            var id = match.Groups["id"].Value;
            var name = match.Groups["name"].Value.Trim();
            var isCurrent = currentBootId is not null && currentBootId.StartsWith(id, StringComparison.OrdinalIgnoreCase);
            entries.Add(new BootEntry(id, name, isCurrent, isCurrent));
        }

        Log.Verbose("Found {Count} candidate startup volumes", entries.Count);
        return entries;
    }

    private static async Task<string?> GetCurrentBootDiskIdAsync(CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("bless", "--getBoot", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return null;
        }

        var device = result.StandardOutput.Trim();
        return device.Replace("/dev/", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetNextBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default)
    {
        Log.Verbose("Setting startup volume to {Id} ({Description})", entry.Id, entry.Description);
        var result = await ProcessRunner.RunAsync("bless", $"--device /dev/{entry.Id} --setBoot", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"bless failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        Log.Information("Startup volume changed to '{Description}' ({Id})", entry.Description, entry.Id);
    }

    public Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("RequestBootToFirmwareSetupAsync invoked on macOS, which has no scriptable firmware setup entry point");
        throw new NotSupportedException(
            "macOS does not support scripting entry into firmware setup. Hold Option (Intel Macs) or the power button (Apple Silicon) during startup instead.");
    }
}
