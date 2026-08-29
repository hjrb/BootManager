using System.Text.RegularExpressions;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Windows implementation backed by bcdedit.exe (firmware boot manager store) for enumeration and
/// next-boot selection, and by the UEFI "OsIndications" firmware variable to request the setup screen.
/// shutdown.exe /fw is deliberately not used: it reports ERROR_ENVVAR_NOT_FOUND (203) on systems where
/// it cannot update OsIndications itself, which hides the real cause from the user.
/// Only bcdedit's section headers and the "identifier" label are localized; BCD element names such as
/// "displayorder", "bootsequence" and "description" are always emitted in English regardless of system
/// locale, so blocks are parsed by key name. Values spanning multiple lines are indented continuation
/// lines that belong to the preceding key. The identifier is the only field matched by position, since
/// its label is localized but it is always the first field of a block.
/// </summary>
public sealed partial class WindowsBootManagerService : IBootManagerService
{
    private static readonly Regex IdentifierRegex = new(@"\{[^{}]+\}", RegexOptions.Compiled);
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

        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var displayOrder = new List<string>();
        var bootSequence = new List<string>();

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
            var fields = ParseFields(lines.Skip(1));
            if (fields.Identifier is not { } identifier)
            {
                continue;
            }

            if (string.Equals(identifier, "{fwbootmgr}", StringComparison.OrdinalIgnoreCase))
            {
                displayOrder.AddRange(GetIdentifiers(fields.Values, "displayorder"));
                bootSequence.AddRange(GetIdentifiers(fields.Values, "bootsequence"));
                continue;
            }

            if (fields.Values.TryGetValue("description", out var description) && description.Count > 0)
            {
                descriptions[identifier] = description[0];
            }
        }

        var nextBootId = bootSequence.FirstOrDefault() ?? displayOrder.FirstOrDefault();

        var entries = displayOrder
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new BootEntry(
                id,
                descriptions.TryGetValue(id, out var description) ? description : id,
                string.Equals(displayOrder[0], id, StringComparison.OrdinalIgnoreCase),
                string.Equals(nextBootId, id, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Log.Verbose("Found {Count} firmware boot entries", entries.Count);
        return entries;
    }

    private static IEnumerable<string> GetIdentifiers(Dictionary<string, List<string>> values, string key) =>
        values.TryGetValue(key, out var list)
            ? list.Select(v => IdentifierRegex.Match(v)).Where(m => m.Success).Select(m => m.Value)
            : [];

    /// <summary>Splits a block into its identifier and its key/value pairs, folding continuation lines into the preceding key.</summary>
    private static (string? Identifier, Dictionary<string, List<string>> Values) ParseFields(IEnumerable<string> fields)
    {
        string? identifier = null;
        string? currentKey = null;
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            var match = KeyValueRegex.Match(field);
            var key = match.Success ? match.Groups[1].Value.Trim() : null;
            var value = (match.Success ? match.Groups[2].Value : field).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (identifier is null)
            {
                identifier = IdentifierRegex.Match(value) is { Success: true } m ? m.Value : null;
                continue;
            }

            currentKey = key ?? currentKey;
            if (currentKey is null)
            {
                continue;
            }

            if (!values.TryGetValue(currentKey, out var list))
            {
                values[currentKey] = list = [];
            }

            list.Add(value);
        }

        return (identifier, values);
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

    public Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default)
    {
        Log.Verbose("Requesting firmware setup screen on next boot via the OsIndications firmware variable");
        WindowsFirmwareVariables.EnableFirmwarePrivilege();

        var supported = WindowsFirmwareVariables.ReadUInt64("OsIndicationsSupported")
            ?? throw new NotSupportedException(
                "This system does not expose the UEFI 'OsIndicationsSupported' variable, so booting into firmware setup cannot be requested. "
                + "The machine is either booted in legacy BIOS/CSM mode or its firmware does not support this feature.");

        if ((supported & WindowsFirmwareVariables.BootToFirmwareUi) == 0)
        {
            throw new NotSupportedException(
                "The firmware of this system reports that it does not support booting into the UEFI setup screen on request. "
                + "Enter setup manually by pressing the vendor-specific key (often F2, F10 or Del) during startup.");
        }

        var current = WindowsFirmwareVariables.ReadUInt64("OsIndications") ?? 0;
        WindowsFirmwareVariables.WriteUInt64("OsIndications", current | WindowsFirmwareVariables.BootToFirmwareUi);

        Log.Information("System configured to open UEFI firmware setup on the next boot");
        return Task.CompletedTask;
    }
}
