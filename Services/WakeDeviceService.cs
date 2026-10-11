namespace BootManager.Services;

/// <summary>Devices that can be configured to wake the computer, and those currently armed.</summary>
/// <param name="Available">Wake-programmable devices not currently armed.</param>
/// <param name="Armed">Devices currently permitted to wake the computer.</param>
public sealed record WakeDeviceLists(IReadOnlyList<string> Available, IReadOnlyList<string> Armed);

/// <summary>Queries and configures Windows and Linux device wake permissions without opening a terminal.</summary>
public static class WakeDeviceService
{
	/// <summary>Whether this platform supports the wake-device configuration interface.</summary>
	public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

	/// <summary>Reads wake-programmable and wake-armed device names as reported by Windows.</summary>
	/// <remarks>
	/// powercfg /devicequery wake_programmable lists devices whose wake permission can be changed;
	/// wake_armed lists those currently enabled. Each output line is a device name, not a table.
	/// Armed names are matched against programmable names so a localized empty-list message is not
	/// treated as an armed device. Device names are preserved verbatim for subsequent powercfg calls.
	/// </remarks>
	public static async Task<WakeDeviceLists> GetDevicesAsync()
	{
		EnsureSupported();
		if (OperatingSystem.IsLinux())
		{
			var devices = await Task.Run(ReadLinuxDevices);
			return new WakeDeviceLists(
				devices.Where(device => !device.Armed).Select(device => device.Label).ToArray(),
				devices.Where(device => device.Armed).Select(device => device.Label).ToArray());
		}

		var programmable = ParseNames(await RunAsync(["/devicequery", "wake_programmable"]));
		var armedNames = ParseNames(await RunAsync(["/devicequery", "wake_armed"]));
		var armed = programmable.Where(name => armedNames.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
		return new WakeDeviceLists(
			programmable.Except(armed, StringComparer.OrdinalIgnoreCase).ToArray(), armed);
	}

	/// <summary>Arms or disarms a device; requires administrator rights and does not suspend the computer.</summary>
	/// <param name="deviceName">The exact device label returned by the platform query.</param>
	/// <param name="armed">True to permit waking, false to remove that permission.</param>
	/// <remarks>/deviceenablewake and /devicedisablewake change only the named device's wake permission.</remarks>
	public static async Task SetArmedAsync(string deviceName, bool armed)
	{
		EnsureSupported();
		if (!ElevationService.IsElevated())
		{
			throw new InvalidOperationException("Changing wake devices requires administrator rights.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
		if (OperatingSystem.IsLinux())
		{
			var devices = await Task.Run(ReadLinuxDevices);
			var device = devices.SingleOrDefault(entry => entry.Label == deviceName);
			if (device.Path is null)
			{
				throw new InvalidOperationException("The wake device is no longer available. Refresh the device list.");
			}

			// Open an existing kernel attribute: never create a file or interpret a caller-supplied path.
			await using var stream = new FileStream(device.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
			await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(armed ? "enabled\n" : "disabled\n"));
			return;
		}

		await RunAsync([armed ? "/deviceenablewake" : "/devicedisablewake", deviceName]);
	}

	/// <summary>Discovers physical sysfs wake controls without following symlinks or changing settings.</summary>
	/// <remarks>
	/// Only power/wakeup attributes containing enabled or disabled are configurable. Empty attributes
	/// are not wake-capable. Product names are optional; the physical path keeps labels unambiguous.
	/// Changes are live only and may reset on reconnection, reboot, or suspend/resume.
	/// </remarks>
	private static List<(string Label, string Path, bool Armed)> ReadLinuxDevices()
	{
		var devices = new List<(string Label, string Path, bool Armed)>();
		var options = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			AttributesToSkip = FileAttributes.ReparsePoint,
		};
		foreach (var path in Directory.EnumerateFiles("/sys/devices", "wakeup", options))
		{
			if (Path.GetFileName(Path.GetDirectoryName(path)) != "power")
			{
				continue;
			}
			try
			{
				var state = File.ReadAllText(path).Trim();
				if (state is not ("enabled" or "disabled"))
				{
					continue;
				}
				var directory = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
				var productPath = Path.Combine(directory, "product");
				var product = File.Exists(productPath) ? File.ReadAllText(productPath).Trim() : Path.GetFileName(directory);
				devices.Add(($"{product} [{directory}]", path, state == "enabled"));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				Serilog.Log.Debug(ex, "Unable to read wake attribute {Path}; device may have disconnected", path);
			}
		}
		return devices.OrderBy(device => device.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	/// <summary>Opens macOS System Settings, where network wake is configured through Energy or Battery.</summary>
	public static async Task OpenMacWakeSettingsAsync()
	{
		if (!OperatingSystem.IsMacOS())
		{
			throw new PlatformNotSupportedException("This settings action is available only on macOS.");
		}
		// open -a selects the named application; settings owns any authorization prompts it requires.
		var result = await ProcessRunner.RunAsync("open", ["-a", "System Settings"]);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException($"Unable to open System Settings: {result.ErrorText}");
		}
	}

	private static void EnsureSupported()
	{
		if (!IsSupported)
		{
			throw new PlatformNotSupportedException("Per-device wake configuration is supported on Windows and Linux.");
		}
	}

	private static async Task<string> RunAsync(string[] arguments)
	{
		var result = await ProcessRunner.RunAsync("powercfg.exe", arguments).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException($"Wake device command failed (exit code {result.ExitCode}): {result.ErrorText}");
		}
		return result.StandardOutput;
	}

	private static string[] ParseNames(string output) => output
		.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
		.Where(name => !string.Equals(name.TrimEnd('.'), "NONE", StringComparison.OrdinalIgnoreCase))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.Order(StringComparer.CurrentCultureIgnoreCase)
		.ToArray();
}