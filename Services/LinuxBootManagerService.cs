using System.Text.RegularExpressions;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Linux implementation of <see cref="IBootManagerService"/>, driven by <c>efibootmgr</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>efibootmgr</c> is the standard tool for reading and writing UEFI boot variables on Linux. It
/// needs root, because it works through <c>/sys/firmware/efi/efivars</c>, which is only writable by
/// root. Without it, the tool exits non-zero and its message is passed on to the user unchanged.
/// </para>
/// <para>
/// <b>Assumptions about the output of <c>efibootmgr -v</c>:</b> the tool prints one line per boot
/// entry plus summary lines for the order and the one-time override. These labels are not localized:
/// <code>
/// BootCurrent: 0001
/// BootOrder: 0001,0003,0000
/// BootNext: 0003
/// Boot0001* Windows Boot Manager	HD(1,GPT,...)/File(\EFI\...)
/// </code>
/// A boot entry line starts with <c>Boot</c> followed by exactly four hex digits, an optional
/// <c>*</c> marking the entry as active, then the description. The description is terminated by a
/// tab, after which the device path follows - that tab is what makes the description safely
/// extractable even though it may itself contain spaces.
/// </para>
/// </remarks>
public sealed partial class LinuxBootManagerService : IBootManagerService
{
    /// <summary>Matches an entry line, capturing its four hex digit id and its human readable description.</summary>
    private static readonly Regex BootEntryRegex = new(@"^Boot(?<id>[0-9A-Fa-f]{4})(?<active>\*)?\s+(?<description>[^\t]*)", RegexOptions.Compiled);

    /// <summary>Matches the persistent boot order, a comma separated list of ids in priority order.</summary>
    private static readonly Regex BootOrderRegex = new(@"^BootOrder:\s*(?<order>[0-9A-Fa-f,]+)", RegexOptions.Compiled);

    /// <summary>Matches the one-time override. This line is absent when no override is armed.</summary>
    private static readonly Regex BootNextRegex = new(@"^BootNext:\s*(?<id>[0-9A-Fa-f]{4})", RegexOptions.Compiled);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BootEntry>> GetBootEntriesAsync(CancellationToken cancellationToken = default)
    {
        // efibootmgr -v
        //   -v / --verbose   also prints each entry's description and device path. Without it the
        //                    output contains only ids, which would leave the UI with no readable names.
        Log.Verbose("Enumerating firmware boot entries via efibootmgr -v");
        var result = await ProcessRunner.RunAsync("efibootmgr", "-v", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"efibootmgr failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        var descriptions = new Dictionary<string, string>();

        // Priority order of all entries; its first element is the default.
        List<string> displayOrder = [];

        // The one-time override, or null when the firmware has none armed.
        string? bootNext = null;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd('\r');

            var orderMatch = BootOrderRegex.Match(trimmed);
            if (orderMatch.Success)
            {
                // "[.. x]" is a collection expression that spreads the split result into a new list.
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

            // Without a BootNext variable the firmware just boots the first entry of BootOrder.
            var isNextBoot = bootNext is not null
                ? string.Equals(bootNext, id, StringComparison.OrdinalIgnoreCase)
                : isCurrentDefault;

            entries.Add(new BootEntry(id, description ?? id, isCurrentDefault, isNextBoot));
        }

        Log.Verbose("Found {Count} firmware boot entries", entries.Count);
        return entries;
    }

    /// <inheritdoc />
    public async Task SetNextBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default)
    {
        // efibootmgr -n 0003
        //   -n <id> / --bootnext <id>   writes the UEFI "BootNext" variable. The firmware boots this
        //                               entry once on the next start and then deletes the variable,
        //                               so BootOrder applies again afterwards. BootOrder is untouched.
        Log.Verbose("Setting one-time next boot entry to {Id} ({Description})", entry.Id, entry.Description);
        var result = await ProcessRunner.RunAsync("efibootmgr", $"-n {entry.Id}", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"efibootmgr failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        Log.Information("Next boot entry changed to '{Description}' ({Id})", entry.Description, entry.Id);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unlike Windows, <c>efibootmgr</c> has no "move to front" option: the boot order can only be
    /// written as a whole. The current order is therefore read first and rewritten with the chosen
    /// entry moved to the front, which preserves the relative order of all remaining entries.
    /// </remarks>
    public async Task SetDefaultBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default)
    {
        Log.Verbose("Setting default boot entry to {Id} ({Description})", entry.Id, entry.Description);

        var entries = await GetBootEntriesAsync(cancellationToken).ConfigureAwait(false);
        var order = new[] { entry.Id }
            .Concat(entries.Select(e => e.Id).Where(id => !string.Equals(id, entry.Id, StringComparison.OrdinalIgnoreCase)));

        // efibootmgr -o 0003,0001,0000
        //   -o <ids> / --bootorder <ids>   replaces the whole "BootOrder" variable with this comma
        //                                  separated list. The first id becomes the default entry.
        var result = await ProcessRunner.RunAsync("efibootmgr", $"-o {string.Join(',', order)}", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"efibootmgr failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        Log.Information("Default boot entry changed to '{Description}' ({Id})", entry.Description, entry.Id);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegated to systemd, which sets the UEFI <c>OsIndications</c> variable itself and then reboots.
    /// This is the reason the call reboots immediately here while the Windows implementation only arms
    /// the request: systemd offers no supported way to set the flag without restarting.
    /// </remarks>
    public async Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default)
    {
        // systemctl reboot --firmware-setup
        //   reboot             restarts the machine.
        //   --firmware-setup   tells the firmware to show its setup screen on the way back up.
        //                      Fails on firmware that does not advertise support for this.
        Log.Verbose("Requesting firmware setup screen on next boot via systemctl reboot --firmware-setup");
        var result = await ProcessRunner.RunAsync("systemctl", "reboot --firmware-setup", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"systemctl failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        Log.Information("System configured to boot into UEFI firmware setup; restart initiated");
    }
}
