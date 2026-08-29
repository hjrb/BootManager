using System.Text.RegularExpressions;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Windows implementation of <see cref="IBootManagerService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Enumeration and boot selection go through <c>bcdedit.exe</c>, the built-in tool for the Boot
/// Configuration Data store. Requesting the firmware setup screen instead writes the UEFI
/// <c>OsIndications</c> variable directly (see <see cref="RequestBootToFirmwareSetupAsync"/>).
/// </para>
/// <para>
/// <b>Why bcdedit and not a NuGet package or an API:</b> Windows exposes no supported managed API for
/// the firmware boot order, and there is no maintained package for it. <c>bcdedit</c> ships with the
/// OS, so relying on it avoids taking a dependency that could break.
/// </para>
/// <para>
/// <b>Assumptions about the output format</b> (relied upon by <see cref="ParseFields"/>):
/// <list type="bullet">
///   <item><description>
///     Blocks are separated by blank lines and start with a header line plus a line of dashes,
///     e.g. "Firmware Application (101fffff)".
///   </description></item>
///   <item><description>
///     Section headers and the "identifier" label <b>are localized</b> - on a German system the label
///     reads "Bezeichner". The identifier is therefore located by position (always the first field of
///     a block) rather than by its label.
///   </description></item>
///   <item><description>
///     BCD element names such as <c>displayorder</c>, <c>bootsequence</c> and <c>description</c> are
///     <b>not localized</b> and can safely be matched literally.
///   </description></item>
///   <item><description>
///     A key and its value are separated by two or more spaces, and a value list continues on
///     following indented lines that carry no key of their own.
///   </description></item>
/// </list>
/// If Microsoft ever localizes the element names, enumeration would silently return no entries.
/// </para>
/// </remarks>
public sealed partial class WindowsBootManagerService : IBootManagerService
{
	/// <summary>
	/// Matches a BCD identifier in braces. This deliberately accepts any content, because identifiers
	/// are either GUIDs (<c>{7e6b4144-...}</c>) or well-known aliases (<c>{bootmgr}</c>, <c>{fwbootmgr}</c>);
	/// an earlier GUID-only pattern silently dropped the alias entries.
	/// </summary>
	private static readonly Regex IdentifierRegex = new(@"\{[^{}]+\}", RegexOptions.Compiled);

	/// <summary>
	/// Splits a line into key and value. The key must start at column 0 and is separated from the
	/// value by at least two spaces, which is how bcdedit aligns its output into columns.
	/// </summary>
	private static readonly Regex KeyValueRegex = new(@"^(\S.*?)\s{2,}(.*)$", RegexOptions.Compiled);

	/// <inheritdoc />
	public async Task<IReadOnlyList<BootEntry>> GetBootEntriesAsync(CancellationToken cancellationToken = default)
	{
		// bcdedit /enum firmware
		//   /enum firmware   lists the entries of the *firmware* boot manager (the UEFI boot menu),
		//                    not the Windows boot menu. This includes the {fwbootmgr} block, which
		//                    holds the boot order, plus one block per bootable firmware application.
		Log.Verbose("Enumerating firmware boot entries via bcdedit /enum firmware");
		var result = await ProcessRunner.RunAsync("bcdedit.exe", "/enum firmware", cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			// Without Administrator rights bcdedit exits with 1 and prints "Access denied" to stdout.
			throw new InvalidOperationException($"bcdedit failed with exit code {result.ExitCode}: {result.StandardError}");
		}

		// Normalize Windows line endings first, so a blank line is reliably "\n\n".
		var blocks = result.StandardOutput
			.Replace("\r\n", "\n")
			.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

		// Identifier -> display name, collected from the individual application blocks.
		var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		// The persistent boot order; its first element is the default entry.
		var displayOrder = new List<string>();

		// The one-time override, if one is currently armed. Usually empty.
		var bootSequence = new List<string>();

		foreach (var block in blocks)
		{
			var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Where(l => !l.TrimStart().StartsWith('-')) // drop the "-----" underline below the header
				.ToList();
			if (lines.Count < 2)
			{
				continue;
			}

			// Skip the header line (e.g. "Firmware Application (101fffff)" / "Firmware Boot Manager").
			var fields = ParseFields(lines.Skip(1));

			// "is not { } identifier" means: skip the block if no identifier could be found.
			if (fields.Identifier is not { } identifier)
			{
				continue;
			}

			// {fwbootmgr} is the firmware boot manager itself: it carries the order, not a bootable OS.
			if (string.Equals(identifier, "{fwbootmgr}", StringComparison.OrdinalIgnoreCase))
			{
				displayOrder.AddRange(GetIdentifiers(fields.Values, "displayorder"));
				bootSequence.AddRange(GetIdentifiers(fields.Values, "bootsequence"));
				continue;
			}

			// Any other block describes one bootable entry; we only need its human readable name.
			if (fields.Values.TryGetValue("description", out var description) && description.Count > 0)
			{
				descriptions[identifier] = description[0];
			}
		}

		// With no one-time override armed, the machine simply boots the first entry of the boot order.
		var nextBootId = bootSequence.FirstOrDefault() ?? displayOrder.FirstOrDefault();

		// Distinct guards against an entry appearing both in displayorder and bootsequence.
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

	/// <summary>
	/// Returns the identifiers stored under a given key, e.g. every entry of <c>displayorder</c>.
	/// </summary>
	/// <returns>The identifiers in the order bcdedit listed them; empty if the key is absent.</returns>
	private static IEnumerable<string> GetIdentifiers(Dictionary<string, List<string>> values, string key) =>
		values.TryGetValue(key, out var list)
			? list.Select(v => IdentifierRegex.Match(v)).Where(m => m.Success).Select(m => m.Value)
			: [];

	/// <summary>
	/// Splits one bcdedit block into its identifier and its key/value pairs.
	/// </summary>
	/// <remarks>
	/// A key may carry several values, which bcdedit prints on additional indented lines without
	/// repeating the key. Those continuation lines are folded into the value list of the preceding
	/// key, which is why the method tracks a "current key" while iterating.
	/// <para>
	/// The identifier is taken from the first field of the block because its label is localized and
	/// therefore cannot be matched by name.
	/// </para>
	/// </remarks>
	/// <param name="fields">The block's lines, without the header and its underline.</param>
	/// <returns>
	/// The block's identifier (<see langword="null"/> if none was found) and its values grouped by key.
	/// </returns>
	private static (string? Identifier, Dictionary<string, List<string>> Values) ParseFields(IEnumerable<string> fields)
	{
		string? identifier = null;
		string? currentKey = null;
		var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

		foreach (var field in fields)
		{
			var match = KeyValueRegex.Match(field);

			// No match means the line is an indented continuation, i.e. a value without its own key.
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

			// Keep the previous key for continuation lines.
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

	/// <inheritdoc />
	public async Task SetNextBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default)
	{
		// bcdedit /set {fwbootmgr} bootsequence {id}
		//   /set              writes an element of a BCD object.
		//   {fwbootmgr}       the object being modified: the firmware boot manager.
		//   bootsequence      the one-time boot list. The firmware works through it on the next start
		//                     and clears it afterwards, so the persistent displayorder is untouched.
		//   {id}              the entry to boot next time.
		Log.Verbose("Setting one-time next boot entry to {Id} ({Description})", entry.Id, entry.Description);
		var result = await ProcessRunner.RunAsync("bcdedit.exe", $"/set {{fwbootmgr}} bootsequence {entry.Id}", cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException($"bcdedit failed with exit code {result.ExitCode}: {result.StandardError}");
		}

		Log.Information("Next boot entry changed to '{Description}' ({Id})", entry.Description, entry.Id);
	}

	/// <inheritdoc />
	public async Task SetDefaultBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default)
	{
		// bcdedit /set {fwbootmgr} displayorder {id} /addfirst
		//   displayorder      the persistent boot order; its first element is the default entry.
		//   /addfirst         moves the given entry to the front instead of replacing the whole list.
		//                     Using /addfirst rather than passing a full list means the remaining
		//                     entries keep their relative order and none can be lost by accident.
		Log.Verbose("Setting default boot entry to {Id} ({Description})", entry.Id, entry.Description);
		var result = await ProcessRunner.RunAsync("bcdedit.exe", $"/set {{fwbootmgr}} displayorder {entry.Id} /addfirst", cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException($"bcdedit failed with exit code {result.ExitCode}: {result.StandardError}");
		}

		Log.Information("Default boot entry changed to '{Description}' ({Id})", entry.Description, entry.Id);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Sets the UEFI <c>OsIndications</c> variable rather than calling <c>shutdown.exe /r /fw</c>.
	/// <para>
	/// <b>Why not shutdown.exe:</b> it fails with the unhelpful error 203
	/// ("The system could not find the environment option that was entered") whenever it cannot update
	/// the variable itself, which hides the real cause. Writing the variable directly also lets the app
	/// check up front whether the firmware supports the feature at all, and it does not force an
	/// immediate reboot - the user chooses when to restart.
	/// </para>
	/// <para>
	/// The firmware advertises what it can do in the read-only <c>OsIndicationsSupported</c> variable
	/// and takes requests in the writable <c>OsIndications</c> variable. Existing bits in
	/// <c>OsIndications</c> are preserved, since other features share the same variable.
	/// </para>
	/// </remarks>
	public Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default)
	{
		Log.Verbose("Requesting firmware setup screen on next boot via the OsIndications firmware variable");

		// Firmware variables are inaccessible until this privilege is switched on, even for admins.
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

		// Read-modify-write: OR in our bit so unrelated requests already stored there survive.
		var current = WindowsFirmwareVariables.ReadUInt64("OsIndications") ?? 0;
		WindowsFirmwareVariables.WriteUInt64("OsIndications", current | WindowsFirmwareVariables.BootToFirmwareUi);

		Log.Information("System configured to open UEFI firmware setup on the next boot");

		// Nothing here is asynchronous, but the interface is task based for the sake of the other platforms.
		return Task.CompletedTask;
	}
}
