using System.Text.RegularExpressions;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Windows implementation backed by bcdedit.exe (firmware boot manager store) and shutdown.exe
/// (to request the firmware setup screen on next boot).
/// bcdedit's field labels are localized, so entries are parsed positionally rather than by label text:
/// for each "Firmware Application" block, the first field is always the identifier GUID and the second
/// is the description. For the "Firmware Boot Manager" block, GUIDs found before the first plain numeric
/// value (the timeout) belong to the persistent display order, and any GUID found after it is the
/// one-time "bootsequence" (i.e. the entry that will be used for the next boot only).
/// </summary>
public sealed partial class WindowsBootManagerService : IBootManagerService
{
    private static readonly Regex GuidRegex = new(@"\{[0-9a-fA-F-]+\}", RegexOptions.Compiled);
    private static readonly Regex KeyValueRegex = new(@"^(\S.*?)\s{2,}(.*)$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<BootEntry>> GetBootEntriesAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("Enumerating firmware boot entries via bcdedit /enum firmware");
        var result = await ProcessRunner.RunAsync("bcdedit.exe", "/enum firmware", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"bcdedit failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        var blocks = result.StandardOutput
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        var descriptions = new Dictionary<string, string>();
        var displayOrder = new List<string>();
        string? bootSequence = null;

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.TrimStart().StartsWith('-'))
                .ToList();
            if (lines.Count < 2)
            {
                continue;
            }

            // Skip the header line (e.g. "Firmware Application (101fffff)" / "Firmware Boot Manager").
            var fields = lines.Skip(1).ToList();

            var isFwBootMgr = fields.Any(f => f.Contains("{fwbootmgr}", StringComparison.OrdinalIgnoreCase));
            if (isFwBootMgr)
            {
                ParseFirmwareBootManagerBlock(fields, displayOrder, ref bootSequence);
                continue;
            }

            ParseFirmwareApplicationBlock(fields, descriptions);
        }

        var entries = new List<BootEntry>();
        foreach (var id in displayOrder)
        {
            descriptions.TryGetValue(id, out var description);
            var isCurrentDefault = displayOrder.Count > 0 && string.Equals(displayOrder[0], id, StringComparison.OrdinalIgnoreCase);
            var isNextBoot = bootSequence is not null
                ? string.Equals(bootSequence, id, StringComparison.OrdinalIgnoreCase)
                : isCurrentDefault;
            entries.Add(new BootEntry(id, description ?? id, isCurrentDefault, isNextBoot));
        }

        Log.Verbose("Found {Count} firmware boot entries", entries.Count);
        return entries;
    }

    private static void ParseFirmwareApplicationBlock(List<string> fields, Dictionary<string, string> descriptions)
    {
        string? identifier = null;
        foreach (var field in fields)
        {
            var value = ExtractValue(field);
            var guidMatch = GuidRegex.Match(value);
            if (identifier is null)
            {
                if (guidMatch.Success)
                {
                    identifier = guidMatch.Value;
                }

                continue;
            }

            // The second populated field is the description.
            descriptions[identifier] = value;
            return;
        }
    }

    private static void ParseFirmwareBootManagerBlock(List<string> fields, List<string> displayOrder, ref string? bootSequence)
    {
        var passedTimeout = false;
        foreach (var field in fields)
        {
            var value = ExtractValue(field);
            var guidMatch = GuidRegex.Match(value);
            if (guidMatch.Success)
            {
                if (string.Equals(guidMatch.Value, "{fwbootmgr}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!passedTimeout)
                {
                    displayOrder.Add(guidMatch.Value);
                }
                else
                {
                    bootSequence = guidMatch.Value;
                }
            }
            else if (int.TryParse(value, out _))
            {
                passedTimeout = true;
            }
        }
    }

    private static string ExtractValue(string field)
    {
        var match = KeyValueRegex.Match(field);
        return match.Success ? match.Groups[2].Value.Trim() : field.Trim();
    }

    public async Task SetNextBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default)
    {
        Log.Verbose("Setting one-time next boot entry to {Id} ({Description})", entry.Id, entry.Description);
        var result = await ProcessRunner.RunAsync("bcdedit.exe", $"/set {{fwbootmgr}} bootsequence {entry.Id}", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"bcdedit failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        Log.Information("Next boot entry changed to '{Description}' ({Id})", entry.Description, entry.Id);
    }

    public async Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("Requesting firmware setup screen on next boot via shutdown /r /fw /t 0");
        var result = await ProcessRunner.RunAsync("shutdown.exe", "/r /fw /t 0", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"shutdown failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        Log.Information("System configured to boot into UEFI firmware setup; restart initiated");
    }
}
