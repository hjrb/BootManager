using System.Text.RegularExpressions;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Linux implementation backed by efibootmgr (enumeration, one-time next boot) and systemd's
/// "systemctl reboot --firmware-setup" (request firmware setup on next boot). Requires root
/// privileges to read/write UEFI NVRAM variables.
/// </summary>
public sealed partial class LinuxBootManagerService : IBootManagerService
{
    private static readonly Regex BootEntryRegex = new(@"^Boot(?<id>[0-9A-Fa-f]{4})(?<active>\*)?\s+(?<description>[^\t]*)", RegexOptions.Compiled);
    private static readonly Regex BootOrderRegex = new(@"^BootOrder:\s*(?<order>[0-9A-Fa-f,]+)", RegexOptions.Compiled);
    private static readonly Regex BootNextRegex = new(@"^BootNext:\s*(?<id>[0-9A-Fa-f]{4})", RegexOptions.Compiled);

    public async Task<IReadOnlyList<BootEntry>> GetBootEntriesAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("Enumerating firmware boot entries via efibootmgr -v");
        var result = await ProcessRunner.RunAsync("efibootmgr", "-v", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"efibootmgr failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        var descriptions = new Dictionary<string, string>();
        List<string> displayOrder = [];
        string? bootNext = null;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd('\r');

            var orderMatch = BootOrderRegex.Match(trimmed);
            if (orderMatch.Success)
            {
                displayOrder = [.. orderMatch.Groups["order"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)];
                continue;
            }

            var nextMatch = BootNextRegex.Match(trimmed);
            if (nextMatch.Success)
            {
                bootNext = nextMatch.Groups["id"].Value;
                continue;
            }

            var entryMatch = BootEntryRegex.Match(trimmed);
            if (entryMatch.Success)
            {
                descriptions[entryMatch.Groups["id"].Value] = entryMatch.Groups["description"].Value.Trim();
            }
        }

        var entries = new List<BootEntry>();
        foreach (var id in displayOrder)
        {
            descriptions.TryGetValue(id, out var description);
            var isCurrentDefault = displayOrder.Count > 0 && string.Equals(displayOrder[0], id, StringComparison.OrdinalIgnoreCase);
            var isNextBoot = bootNext is not null
                ? string.Equals(bootNext, id, StringComparison.OrdinalIgnoreCase)
                : isCurrentDefault;
            entries.Add(new BootEntry(id, description ?? id, isCurrentDefault, isNextBoot));
        }

        Log.Verbose("Found {Count} firmware boot entries", entries.Count);
        return entries;
    }

    public async Task SetNextBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default)
    {
        Log.Verbose("Setting one-time next boot entry to {Id} ({Description})", entry.Id, entry.Description);
        var result = await ProcessRunner.RunAsync("efibootmgr", $"-n {entry.Id}", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"efibootmgr failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        Log.Information("Next boot entry changed to '{Description}' ({Id})", entry.Description, entry.Id);
    }

    public async Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("Requesting firmware setup screen on next boot via systemctl reboot --firmware-setup");
        var result = await ProcessRunner.RunAsync("systemctl", "reboot --firmware-setup", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"systemctl failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        Log.Information("System configured to boot into UEFI firmware setup; restart initiated");
    }
}
