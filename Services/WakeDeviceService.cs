namespace BootManager.Services;

/// <summary>Devices that can be configured to wake the computer, and those currently armed.</summary>
/// <param name="Available">Wake-programmable devices not currently armed.</param>
/// <param name="Armed">Devices currently permitted to wake the computer.</param>
public sealed record WakeDeviceLists(IReadOnlyList<string> Available, IReadOnlyList<string> Armed)
{
	/// <summary>Device types indexed by the exact device label; unknown devices remain in Other.</summary>
	public IReadOnlyDictionary<string, string> DeviceTypes { get; init; } = new Dictionary<string, string>();

	/// <summary>Exact device names reported by Windows as capable of waking from any sleep state.</summary>
	public IReadOnlySet<string> WakeFromAny { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Queries and configures Windows and Linux device wake permissions without opening a terminal.</summary>
public static class WakeDeviceService
{
	/// <summary>Returns the Windows OEM code page used by powercfg for redirected device names.</summary>
	[System.Runtime.InteropServices.DllImport("kernel32.dll")]
	private static extern uint GetOEMCP();

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
				devices.Where(device => device.Armed).Select(device => device.Label).ToArray())
			{
				DeviceTypes = devices.ToDictionary(device => device.Label, device => GetLinuxDeviceType(device.Path)),
			};
		}

		var programmable = ParseNames(await RunAsync(["/devicequery", "wake_programmable"]));
		var armedNames = ParseNames(await RunAsync(["/devicequery", "wake_armed"]));
		var wakeFromAny = ParseNames(await RunAsync(["/devicequery", "wake_from_any"]));
		var armed = programmable.Where(name => armedNames.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
		return new WakeDeviceLists(
			programmable.Except(armed, StringComparer.OrdinalIgnoreCase).ToArray(), armed)
		{
			DeviceTypes = await GetWindowsDeviceTypesAsync(programmable),
			WakeFromAny = wakeFromAny.ToHashSet(StringComparer.OrdinalIgnoreCase),
		};
	}

	/// <summary>Uses locale-independent Windows PnP classes rather than guessing from translated device names.</summary>
	private static async Task<IReadOnlyDictionary<string, string>> GetWindowsDeviceTypesAsync(string[] deviceNames)
	{
		var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			// Query Unicode inventory through JSON. Force UTF-8 for this PowerShell process only.
			var script = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); "
				+ "$ErrorActionPreference = 'Stop'; ConvertTo-Json -Compress -InputObject "
				+ "@(Get-CimInstance Win32_PnPEntity | Select-Object Name, PNPClass)";
			var result = await ProcessRunner.RunAsync("powershell.exe",
				["-NoProfile", "-NonInteractive", "-Command", script], outputEncoding: System.Text.Encoding.UTF8);
			if (!result.Succeeded)
			{
				throw new InvalidOperationException(result.ErrorText);
			}
			using var inventory = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
			var classes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in inventory.RootElement.EnumerateArray())
			{
				var name = entry.GetProperty("Name").GetString();
				var deviceClass = entry.GetProperty("PNPClass").GetString();
				if (!string.IsNullOrWhiteSpace(name))
				{
					classes.TryAdd(name, deviceClass ?? string.Empty);
				}
			}
			foreach (var name in deviceNames)
			{
				// powercfg appends numeric suffixes to distinguish identically named devices.
				var baseName = System.Text.RegularExpressions.Regex.Replace(name, @" \(\d+\)$", string.Empty);
				if (classes.TryGetValue(name, out var deviceClass) || classes.TryGetValue(baseName, out deviceClass))
				{
					types[name] = deviceClass.ToUpperInvariant() switch
					{
						"MOUSE" => "Mice",
						"KEYBOARD" => "Keyboards",
						"NET" => "Network",
						"USB" or "HIDCLASS" => "USB / HID",
						_ => "Other",
					};
				}
			}
		}
		catch (Exception ex)
		{
			Serilog.Log.Warning(ex, "Device class query failed; unclassified wake devices will appear under Other");
		}
		return types;
	}

	private static string GetLinuxDeviceType(string wakePath)
	{
		var directory = Path.GetDirectoryName(Path.GetDirectoryName(wakePath))!;
		if (Directory.Exists(Path.Combine(directory, "net")))
		{
			return "Network";
		}
		if (wakePath.Contains("/usb", StringComparison.Ordinal))
		{
			return "USB / HID";
		}
		return "Other";
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
		System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
		var encoding = System.Text.Encoding.GetEncoding((int)GetOEMCP());
		var result = await ProcessRunner.RunAsync("powercfg.exe", arguments, outputEncoding: encoding).ConfigureAwait(false);
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