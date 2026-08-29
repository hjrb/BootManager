using System.Text.RegularExpressions;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// macOS implementation of <see cref="IBootManagerService"/>, driven by <c>diskutil</c> and <c>bless</c>.
/// </summary>
/// <remarks>
/// <para>
/// Apple firmware does not follow the generic UEFI boot variable model, so this implementation differs
/// from the other two in two important ways:
/// <list type="bullet">
///   <item><description>
///     There is no one-time boot override. <c>bless --setBoot</c> changes the startup disk
///     permanently, so <see cref="SetNextBootEntryAsync"/> and <see cref="SetDefaultBootEntryAsync"/>
///     do the same thing, and every entry reports the same value for both flags.
///   </description></item>
///   <item><description>
///     There is no firmware setup screen to boot into, so
///     <see cref="RequestBootToFirmwareSetupAsync"/> always fails with a message explaining the manual
///     alternative.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// "Boot entries" here are really bootable volumes, since that is the unit macOS works with. Changes
/// require root and, on recent systems, may additionally be blocked by System Integrity Protection.
/// </para>
/// </remarks>
public sealed partial class MacBootManagerService : IBootManagerService
{
	/// <summary>
	/// Matches a volume line of <c>diskutil list</c>, e.g.
	/// <c>   2:                 Apple_APFS Container disk1         500.0 GB   disk0s2</c>.
	/// Captures the volume name and the device node, relying on diskutil's fixed column layout:
	/// the name is followed by two or more spaces, then the size, its unit, and the identifier.
	/// </summary>
	private static readonly Regex VolumeLineRegex = new(
		@"^\s*\d+:\s+\S+\s+(?<name>.+?)\s{2,}[\d.]+\s+\w{2}\s+(?<id>disk\S+)\s*$",
		RegexOptions.Compiled);

	/// <inheritdoc />
	public async Task<IReadOnlyList<BootEntry>> GetBootEntriesAsync(CancellationToken cancellationToken = default)
	{
		// diskutil list
		//   list   prints every disk and its partitions/volumes in a fixed-width table. There is no
		//          option that filters for bootable volumes, so the caller sees all of them.
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

			// StartsWith rather than an exact match: bless may report a sub-volume of the container.
			var isCurrent = currentBootId is not null && currentBootId.StartsWith(id, StringComparison.OrdinalIgnoreCase);

			// Both flags get the same value because macOS has no one-time boot override.
			entries.Add(new BootEntry(id, name, isCurrent, isCurrent));
		}

		Log.Verbose("Found {Count} candidate startup volumes", entries.Count);
		return entries;
	}

	/// <summary>
	/// Reads which device the machine is currently set to boot from.
	/// </summary>
	/// <returns>
	/// The device node without the <c>/dev/</c> prefix, or <see langword="null"/> if it could not be
	/// determined. A failure here is not fatal: the list is still usable, it just has no entry marked
	/// as current, so no exception is raised.
	/// </returns>
	private static async Task<string?> GetCurrentBootDiskIdAsync(CancellationToken cancellationToken)
	{
		// bless --getBoot
		//   --getBoot   prints the device that is currently selected as the startup disk,
		//               e.g. "/dev/disk1s1".
		var result = await ProcessRunner.RunAsync("bless", "--getBoot", cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			return null;
		}

		var device = result.StandardOutput.Trim();
		return device.Replace("/dev/", string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	/// <inheritdoc />
	/// <remarks>
	/// On macOS this is <b>not</b> a one-time override - the change is permanent, because the platform
	/// offers nothing equivalent to a UEFI <c>BootNext</c> variable.
	/// </remarks>
	public async Task SetNextBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default)
	{
		// bless --device /dev/disk0s2 --setBoot
		//   --device <node>   the volume to boot from.
		//   --setBoot         makes that volume the active startup disk. This is persistent; macOS
		//                     has no option for a single-boot override.
		Log.Verbose("Setting startup volume to {Id} ({Description})", entry.Id, entry.Description);
		var result = await ProcessRunner.RunAsync("bless", $"--device /dev/{entry.Id} --setBoot", cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException($"bless failed with exit code {result.ExitCode}: {result.StandardError}");
		}

		Log.Information("Startup volume changed to '{Description}' ({Id})", entry.Description, entry.Id);
	}

	/// <inheritdoc />
	/// <remarks>
	/// macOS has no one-time boot override, so "default" and "next boot" are the same persistent
	/// operation and this simply forwards to <see cref="SetNextBootEntryAsync"/>.
	/// </remarks>
	public Task SetDefaultBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default) =>
		SetNextBootEntryAsync(entry, cancellationToken);

	/// <inheritdoc />
	/// <remarks>
	/// Always throws. Apple provides no command or NVRAM variable that opens a firmware setup screen -
	/// Macs simply do not have a user-facing one. The startup picker is reached by holding a key during
	/// power-on, which cannot be triggered from software.
	/// </remarks>
	public Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default)
	{
		Log.Verbose("RequestBootToFirmwareSetupAsync invoked on macOS, which has no scriptable firmware setup entry point");
		throw new NotSupportedException(
			"macOS does not support scripting entry into firmware setup. Hold Option (Intel Macs) or the power button (Apple Silicon) during startup instead.");
	}
}
