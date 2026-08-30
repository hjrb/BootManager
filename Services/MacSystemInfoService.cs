using System.Text.RegularExpressions;
using BootManager.Models;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Collects boot diagnostics on macOS.
/// </summary>
/// <remarks>
/// macOS exposes hardware and firmware details through <c>system_profiler</c> and kernel state through
/// <c>sysctl</c>. Several concepts from the other platforms have no counterpart here: there is no
/// firmware setup screen, no UEFI boot order, and no per-phase boot timing, so those items are
/// reported as "not applicable" rather than silently omitted.
/// </remarks>
public sealed class MacSystemInfoService : ISystemInfoService
{
	/// <summary>
	/// Extracts a "Label: Value" pair from <c>system_profiler</c> output, which is an indented,
	/// colon separated list rather than a machine readable format.
	/// </summary>
	private static readonly Regex ProfilerLineRegex = new(@"^\s*(?<label>[^:]+):\s*(?<value>.+?)\s*$", RegexOptions.Compiled);

	/// <inheritdoc />
	public async Task<IReadOnlyList<SystemInfoItem>> GetSystemInfoAsync(CancellationToken cancellationToken = default)
	{
		Log.Verbose("Collecting macOS boot diagnostics");

		var hardware = await GetHardwareOverviewAsync(cancellationToken).ConfigureAwait(false);

		var items = new List<SystemInfoItem>(CommonSystemInfo.GetRuntimeFacts())
		{
			// Macs boot through Apple's own firmware, which is UEFI derived but does not expose the
			// generic boot variables, so the concept of a "boot mode" does not apply.
			new(CommonSystemInfo.FirmwareCategory, "Boot mode", "Apple firmware (no user-configurable UEFI boot entries)"),
			new(CommonSystemInfo.FirmwareCategory, "Firmware version", FirstOf(hardware, "System Firmware Version", "Boot ROM Version")),
			new(CommonSystemInfo.FirmwareCategory, "OS loader version", FirstOf(hardware, "OS Loader Version")),
			new(CommonSystemInfo.FirmwareCategory, "Secure Boot", await GetSecureBootStateAsync(cancellationToken).ConfigureAwait(false)),
			new(CommonSystemInfo.HardwareCategory, "Manufacturer", "Apple"),
			new(CommonSystemInfo.HardwareCategory, "Model", FirstOf(hardware, "Model Name", "Model Identifier")),
			new(CommonSystemInfo.HardwareCategory, "Chip", FirstOf(hardware, "Chip", "Processor Name")),
			new(CommonSystemInfo.HardwareCategory, "Serial number", FirstOf(hardware, "Serial Number (system)")),

			// macOS keeps no record of how long a boot took; only the moment it happened is available.
			new(CommonSystemInfo.BootTimingCategory, "Last boot duration", "Not recorded by macOS"),
		};

		Log.Verbose("Collected {Count} macOS diagnostic values", items.Count);
		return items;
	}

	/// <summary>
	/// Reads the hardware overview as a lookup of its labels and values.
	/// </summary>
	/// <returns>
	/// The parsed key/value pairs, or an empty dictionary if the tool failed. An empty result is
	/// harmless: every lookup then falls back to "Unknown".
	/// </returns>
	private static async Task<Dictionary<string, string>> GetHardwareOverviewAsync(CancellationToken cancellationToken)
	{
		// system_profiler SPHardwareDataType
		//   SPHardwareDataType   limits the report to the hardware section. Without a data type the
		//                        tool produces a very large report and takes several seconds.
		var result = await ProcessRunner.RunAsync("system_profiler", "SPHardwareDataType", cancellationToken).ConfigureAwait(false);

		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!result.Succeeded)
		{
			return values;
		}

		foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			var match = ProfilerLineRegex.Match(line.TrimEnd('\r'));
			if (match.Success)
			{
				values[match.Groups["label"].Value.Trim()] = match.Groups["value"].Value;
			}
		}

		return values;
	}

	/// <summary>
	/// Reports the state of System Integrity Protection.
	/// </summary>
	/// <remarks>
	/// SIP is the closest equivalent to Secure Boot on a Mac and is the reason a
	/// <c>bless --setBoot</c> call can be refused even when running as root, which makes it worth
	/// showing here.
	/// </remarks>
	private static async Task<string> GetSecureBootStateAsync(CancellationToken cancellationToken)
	{
		try
		{
			// csrutil status
			//   status   prints "System Integrity Protection status: enabled." or "... disabled."
			var result = await ProcessRunner.RunAsync("csrutil", "status", cancellationToken).ConfigureAwait(false);
			var output = (result.StandardOutput + result.StandardError).Trim();

			if (output.Contains("enabled", StringComparison.OrdinalIgnoreCase))
			{
				return "System Integrity Protection enabled (may block changes to the startup disk)";
			}

			return output.Contains("disabled", StringComparison.OrdinalIgnoreCase)
				? "System Integrity Protection disabled"
				: CommonSystemInfo.Unknown;
		}
		catch (Exception ex)
		{
			Log.Information(ex, "csrutil is not available, SIP state cannot be determined");
			return CommonSystemInfo.Unknown;
		}
	}

	/// <summary>
	/// Returns the first of the given labels that is present, since the wording of
	/// <c>system_profiler</c> differs between Intel and Apple Silicon machines and between OS versions.
	/// </summary>
	private static string FirstOf(Dictionary<string, string> values, params string[] labels)
	{
		foreach (var label in labels)
		{
			if (values.TryGetValue(label, out var value) && value.Length > 0)
			{
				return value;
			}
		}

		return CommonSystemInfo.Unknown;
	}
}
