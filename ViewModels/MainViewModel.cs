using System.Collections.ObjectModel;
using BootManager.Models;
using BootManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BootManager.ViewModels;

/// <summary>
/// Backs the main window: exposes the boot entries, the system diagnostics, and the actions the user
/// can trigger.
/// </summary>
/// <remarks>
/// <para>
/// This class is <c>partial</c> because the CommunityToolkit MVVM source generator writes the other
/// half at compile time. Fields marked <c>[ObservableProperty]</c> gain change notification so the UI
/// updates automatically, and methods marked <c>[RelayCommand]</c> gain a matching <c>...Command</c>
/// property that XAML can bind a button to - for example <c>RefreshAsync</c> becomes
/// <c>RefreshCommand</c>.
/// </para>
/// <para>
/// Every action runs through <see cref="RunGuardedAsync"/>, which is what fulfils the rule that no
/// failure may crash the application or block the user with a modal dialog.
/// </para>
/// </remarks>
public partial class MainViewModel : ViewModelBase
{
	private readonly IBootManagerService _bootManagerService;
	private readonly ISystemInfoService _systemInfoService;

	/// <summary>
	/// Parameterless constructor used by the XAML designer, which cannot supply dependencies.
	/// </summary>
	public MainViewModel()
		: this(BootManagerServiceFactory.Create(), BootManagerServiceFactory.CreateSystemInfoService())
	{
	}

	/// <summary>Creates the view model with the platform services it should work against.</summary>
	public MainViewModel(IBootManagerService bootManagerService, ISystemInfoService systemInfoService)
	{
		_bootManagerService = bootManagerService;
		_systemInfoService = systemInfoService;

		IsElevated = ElevationService.IsElevated();
		if (!IsElevated)
		{
			ElevationMessage = $"Not running as {ElevationService.RequiredPrivilegeName}. Boot options cannot be read or changed until the application is restarted with those privileges.";
			Log.Information("Application is not running with elevated privileges ({Privilege})", ElevationService.RequiredPrivilegeName);
		}

		// Fire and forget: the constructor cannot await, and failures are handled inside the command.
		// Enumeration is skipped without privileges: it could only fail, and greeting the user with a red
		// tool error on top of the banner that already explains the situation helps nobody.
		if (IsElevated)
		{
			_ = RefreshCommand.ExecuteAsync(null);
		}

		_ = RefreshSystemInfoCommand.ExecuteAsync(null);
		if (WakeDevicesSupported)
		{
			_ = RefreshWakeDevicesCommand.ExecuteAsync(null);
		}
	}

	/// <summary>The boot entries offered by the firmware, in boot order.</summary>
	public ObservableCollection<BootEntry> BootEntries { get; } = [];

	/// <summary>Diagnostic facts about the machine, shown on the system information tab.</summary>
	public ObservableCollection<SystemInfoItem> SystemInfo { get; } = [];

	/// <summary>Diagnostic facts grouped by category for the expandable system information view.</summary>
	public ObservableCollection<SystemInfoGroup> SystemInfoGroups { get; } = [];

	/// <summary>Devices that can be armed but currently have wake permission disabled.</summary>
	public ObservableCollection<string> AvailableWakeDevices { get; } = [];

	/// <summary>Devices currently allowed to wake the computer.</summary>
	public ObservableCollection<string> ArmedWakeDevices { get; } = [];

	/// <summary>Available wake devices grouped by their hardware type.</summary>
	public IReadOnlyList<WakeDeviceGroup> AvailableWakeDeviceGroups { get; private set; } = [];

	/// <summary>Armed wake devices grouped by their hardware type.</summary>
	public IReadOnlyList<WakeDeviceGroup> ArmedWakeDeviceGroups { get; private set; } = [];

	/// <summary>Whether any unarmed wake-programmable device is available.</summary>
	public bool HasAvailableWakeDevices => AvailableWakeDevices.Count > 0;

	/// <summary>Whether any device is currently armed.</summary>
	public bool HasArmedWakeDevices => ArmedWakeDevices.Count > 0;

	/// <summary>Whether wake-device configuration is supported by this platform.</summary>
	public bool WakeDevicesSupported => WakeDeviceService.IsSupported;

	/// <summary>Whether wake behavior is managed through macOS System Settings.</summary>
	public bool UsesMacWakeSettings => OperatingSystem.IsMacOS();

	/// <summary>Describes the privilege needed for wake permission changes.</summary>
	public string WakeDevicePrivilegeMessage => $"{ElevationService.RequiredPrivilegeName} rights are required to change wake permissions.";

	/// <summary>Opens macOS settings for network wake configuration.</summary>
	[RelayCommand]
	private async Task OpenWakeSettingsAsync() => await RunGuardedAsync(WakeDeviceService.OpenMacWakeSettingsAsync);

	/// <summary>The available device or group selected in the tree; only device labels can be armed.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ArmWakeDeviceCommand))]
	public partial object? SelectedAvailableWakeDevice { get; set; }

	/// <summary>The armed device or group selected in the tree; only device labels can be disarmed.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DisarmWakeDeviceCommand))]
	public partial object? SelectedArmedWakeDevice { get; set; }

	/// <summary>Whether a wake-device query or change is in progress.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(RefreshWakeDevicesCommand))]
	[NotifyCanExecuteChangedFor(nameof(ArmWakeDeviceCommand))]
	[NotifyCanExecuteChangedFor(nameof(DisarmWakeDeviceCommand))]
	public partial bool IsWakeDeviceBusy { get; set; }

	/// <summary>
	/// The help text of the command line interface, shown verbatim on the "Command Line" tab.
	/// </summary>
	/// <remarks>
	/// Taken from the console code path rather than duplicated here, so the tab cannot fall behind when
	/// a command is added or reworded.
	/// </remarks>
	public string CommandLineHelpText { get; } = CommandLineInterface.GetUsageText();

	/// <summary>The entry the user has selected in the list, or null while nothing is selected.</summary>
	[ObservableProperty]
	public partial BootEntry? SelectedEntry { get; set; }

	/// <summary>
	/// Whether the application has the privileges it needs. Defaults to true so the warning banner does
	/// not flash up before the real check has run.
	/// </summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
	[NotifyCanExecuteChangedFor(nameof(ArmWakeDeviceCommand))]
	[NotifyCanExecuteChangedFor(nameof(DisarmWakeDeviceCommand))]
	public partial bool IsElevated { get; set; } = true;

	/// <summary>Explains the consequences of missing privileges; shown in the warning banner.</summary>
	[ObservableProperty]
	public partial string ElevationMessage { get; set; } = string.Empty;

	/// <summary>Caption of the restart button, which differs per platform.</summary>
	public string RestartElevatedLabel => ElevationService.RestartActionLabel;

	/// <summary>Reboot actions supported by this OS, shown in the toolbar's reboot section.</summary>
	public IReadOnlyList<PowerActionItem> RebootActions => GetPowerActions(PowerActionCategory.Reboot);

	/// <summary>Sleep actions supported by this OS, shown in the toolbar's sleep section.</summary>
	public IReadOnlyList<PowerActionItem> SleepActions => GetPowerActions(PowerActionCategory.Sleep);

	/// <summary>Shutdown actions supported by this OS, shown in the toolbar's shutdown section.</summary>
	public IReadOnlyList<PowerActionItem> ShutdownActions => GetPowerActions(PowerActionCategory.Shutdown);

	/// <summary>Sign-out actions available for the current desktop session.</summary>
	public IReadOnlyList<PowerActionItem> LogoffActions => GetPowerActions(PowerActionCategory.Logoff);

	/// <summary>Whether the current desktop session can be signed out.</summary>
	public bool HasLogoffActions => LogoffActions.Count > 0;

	/// <summary>Whether this OS offers any reboot action; hides the toolbar section when it does not.</summary>
	public bool HasRebootActions => RebootActions.Count > 0;

	/// <summary>Whether this OS offers any sleep action; hides the toolbar section when it does not.</summary>
	public bool HasSleepActions => SleepActions.Count > 0;

	/// <summary>Whether this OS offers any shutdown action; hides the toolbar section when it does not.</summary>
	public bool HasShutdownActions => ShutdownActions.Count > 0;

	/// <summary>Whether this OS offers any power action at all; drives the divider before the UEFI section.</summary>
	public bool HasPowerActions => SystemPowerService.GetSupportedActions().Count > 0;

	private static IReadOnlyList<PowerActionItem> GetPowerActions(PowerActionCategory category) =>
		[.. SystemPowerService.GetSupportedActions()
			.Where(action => SystemPowerService.GetCategory(action) == category)
			.Select(PowerActionItem.Create)];

	/// <summary>Tooltip for the refresh button on the boot options tab.</summary>
	public string RefreshTooltip =>
		$"Re-reads the boot entries from the firmware.\n\nRuns: {_bootManagerService.EnumerateCommand}";

	/// <summary>Tooltip for the "Set as Next Boot" button.</summary>
	public string NextBootTooltip =>
		"One-time override: applies to the next boot only, then reverts to the default.\n\n"
		+ $"Runs: {_bootManagerService.SetNextBootCommand}";

	/// <summary>Tooltip for the "Set as Default" button.</summary>
	public string DefaultBootTooltip =>
		"Persistent: applies to every boot until changed again.\n\n"
		+ $"Runs: {_bootManagerService.SetDefaultBootCommand}";

	/// <summary>Tooltip for the "Open UEFI Setup at Next Boot" button.</summary>
	public string FirmwareSetupTooltip =>
		(FirmwareSetupRestartsImmediately
			? "Restarts the machine into the firmware setup screen, after a countdown. "
			: "Asks the firmware to open its setup screen on the next boot. Does not restart the machine. ")
		+ $"\n\nRuns: {_bootManagerService.FirmwareSetupCommand}";

	/// <summary>Whether an operation is in progress; drives the progress bar.</summary>
	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	/// <summary>The message in the notification banner, or null when nothing is being shown.</summary>
	[ObservableProperty]
	public partial string? NotificationMessage { get; set; }

	/// <summary>Whether the current notification reports a failure, which selects its colour.</summary>
	[ObservableProperty]
	public partial bool IsNotificationError { get; set; }

	private bool CanRefreshWakeDevices() => WakeDevicesSupported && !IsWakeDeviceBusy;
	private bool CanArmWakeDevice() => CanRefreshWakeDevices() && IsElevated && SelectedAvailableWakeDevice is WakeDeviceItem;
	private bool CanDisarmWakeDevice() => CanRefreshWakeDevices() && IsElevated && SelectedArmedWakeDevice is WakeDeviceItem;

	/// <summary>Refreshes both wake-device lists without changing device permissions.</summary>
	[RelayCommand(CanExecute = nameof(CanRefreshWakeDevices))]
	private async Task RefreshWakeDevicesAsync() => await RunWakeDeviceOperationAsync(null, false);

	/// <summary>Allows the selected available device to wake the computer.</summary>
	[RelayCommand(CanExecute = nameof(CanArmWakeDevice))]
	private async Task ArmWakeDeviceAsync() => await RunWakeDeviceOperationAsync((SelectedAvailableWakeDevice as WakeDeviceItem)?.Name, true);

	/// <summary>Removes the selected armed device's wake permission.</summary>
	[RelayCommand(CanExecute = nameof(CanDisarmWakeDevice))]
	private async Task DisarmWakeDeviceAsync() => await RunWakeDeviceOperationAsync((SelectedArmedWakeDevice as WakeDeviceItem)?.Name, false);

	private async Task RunWakeDeviceOperationAsync(string? deviceName, bool armed)
	{
		if (IsWakeDeviceBusy)
		{
			return;
		}
		IsWakeDeviceBusy = true;
		try
		{
			if (deviceName is not null)
			{
				await WakeDeviceService.SetArmedAsync(deviceName, armed);
				Log.Information("Wake permission for {Device} {State}", deviceName, armed ? "enabled" : "disabled");
				ShowNotification($"{deviceName}: wake permission {(armed ? "enabled" : "disabled")}.", isError: false);
			}

			var devices = await WakeDeviceService.GetDevicesAsync();
			SelectedAvailableWakeDevice = null;
			SelectedArmedWakeDevice = null;
			AvailableWakeDevices.Clear();
			ArmedWakeDevices.Clear();
			foreach (var device in devices.Available)
			{
				AvailableWakeDevices.Add(device);
			}
			foreach (var device in devices.Armed)
			{
				ArmedWakeDevices.Add(device);
			}
			AvailableWakeDeviceGroups = GroupWakeDevices(devices.Available, devices.DeviceTypes, devices.WakeFromAny);
			ArmedWakeDeviceGroups = GroupWakeDevices(devices.Armed, devices.DeviceTypes, devices.WakeFromAny);
			OnPropertyChanged(nameof(AvailableWakeDeviceGroups));
			OnPropertyChanged(nameof(ArmedWakeDeviceGroups));
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Wake device operation failed");
			ShowNotification(ex.Message, isError: true);
		}
		finally
		{
			IsWakeDeviceBusy = false;
			OnPropertyChanged(nameof(HasAvailableWakeDevices));
			OnPropertyChanged(nameof(HasArmedWakeDevices));
		}
	}

	private static IReadOnlyList<WakeDeviceGroup> GroupWakeDevices(IReadOnlyList<string> names, IReadOnlyDictionary<string, string> types, IReadOnlySet<string> wakeFromAny) =>
		[.. names.GroupBy(name => types.TryGetValue(name, out var type) ? type : "Other")
			.OrderBy(group => group.Key switch { "Mice" => 0, "Keyboards" => 1, "Network" => 2, "USB / HID" => 3, _ => 4 })
			.Select(group => new WakeDeviceGroup(group.Key, group.Order(StringComparer.CurrentCultureIgnoreCase)
				.Select(name => new WakeDeviceItem(name, wakeFromAny.Contains(name))).ToArray()))];

	/// <summary>Re-reads the boot entries and re-selects the one that will be used for the next boot.</summary>
	[RelayCommand(CanExecute = nameof(IsElevated))]
	private async Task RefreshAsync()
	{
		// Reading firmware variables is privileged on every platform, so refuse with a clear message
		// rather than letting the platform tool fail with a cryptic exit code.
		if (!IsElevated)
		{
			Log.Information("Refresh refused: missing {Privilege} privileges", ElevationService.RequiredPrivilegeName);
			ShowNotification(ElevationMessage, isError: true);
			return;
		}

		await RunGuardedAsync(async () =>
		{
			Log.Verbose("Refreshing boot entries list");
			var entries = await _bootManagerService.GetBootEntriesAsync();

			BootEntries.Clear();
			foreach (var entry in entries)
			{
				BootEntries.Add(entry);
			}

			SelectedEntry = BootEntries.FirstOrDefault(e => e.IsNextBoot) ?? BootEntries.FirstOrDefault();
		});
	}

	/// <summary>
	/// Applies the selected entry as a one-time override for the next boot.
	/// </summary>
	/// <remarks>The list is reloaded afterwards so the "next boot" marker reflects the new state.</remarks>
	[RelayCommand]
	private async Task ApplyNextBootAsync()
	{
		if (SelectedEntry is null)
		{
			return;
		}

		await RunGuardedAsync(async () =>
		{
			await _bootManagerService.SetNextBootEntryAsync(SelectedEntry);
			ShowNotification($"Next boot set to '{SelectedEntry.Description}'.", isError: false);
			await RefreshAsync();
		});
	}

	/// <summary>Makes the selected entry the persistent default used for every boot.</summary>
	[RelayCommand]
	private async Task ApplyDefaultBootAsync()
	{
		if (SelectedEntry is null)
		{
			return;
		}

		await RunGuardedAsync(async () =>
		{
			await _bootManagerService.SetDefaultBootEntryAsync(SelectedEntry);
			ShowNotification($"Default boot entry set to '{SelectedEntry.Description}'.", isError: false);
			await RefreshAsync();
		});
	}

	/// <summary>
	/// Asks the firmware to show its setup screen on the next boot.
	/// </summary>
	/// <remarks>
	/// On Linux this also restarts the machine, which is why the view puts a countdown in front of it;
	/// see <see cref="FirmwareSetupRestartsImmediately"/>.
	/// </remarks>
	[RelayCommand]
	private async Task ConfigureFirmwareSetupAsync()
	{
		await RunGuardedAsync(async () =>
		{
			await _bootManagerService.RequestBootToFirmwareSetupAsync();
			ShowNotification(
				FirmwareSetupRestartsImmediately
					? "Restarting into UEFI firmware setup."
					: "System configured to open UEFI firmware setup on next boot.",
				isError: false);
		});
	}

	/// <summary>
	/// Whether <see cref="ConfigureFirmwareSetupCommand"/> restarts the machine as a side effect.
	/// </summary>
	public bool FirmwareSetupRestartsImmediately => _bootManagerService.FirmwareSetupRestartsImmediately;

	/// <summary>The command the firmware setup request runs, shown in tooltips and in the countdown window.</summary>
	public string FirmwareSetupCommand => _bootManagerService.FirmwareSetupCommand;

	/// <summary>Executes a shutdown or reboot action chosen from the toolbar.</summary>
	public async Task ExecutePowerActionAsync(PowerActionKind action)
	{
		if (action == PowerActionKind.Hibernate && OperatingSystem.IsWindows()
			&& !SystemPowerService.IsWindowsHibernateAvailable())
		{
			ShowNotification(IsElevated
				? "Hibernate is disabled or unavailable. Enable hibernation before using it."
				: "Hibernate is disabled or unavailable, and you are not running as Administrator.", isError: true);
			OnPropertyChanged(nameof(SleepActions));
			return;
		}

		if (action == PowerActionKind.DelayedReboot)
		{
			return;
		}

		await RunGuardedAsync(async () =>
		{
			await SystemPowerService.ExecuteAsync(action).ConfigureAwait(false);
			ShowNotification($"{SystemPowerService.GetFullName(action)} started.", isError: false);
		});
	}

	/// <summary>Changes Windows hibernation only when elevated, reporting the outcome and refreshing the toolbar.</summary>
	/// <param name="enabled">Whether the user confirmed enabling or disabling hibernation.</param>
	public async Task ConfigureWindowsHibernateAsync(bool enabled)
	{
		if (!IsElevated)
		{
			ShowNotification("Changing hibernation requires administrator rights. You are not running as Administrator.", isError: true);
			return;
		}

		await RunGuardedAsync(async () =>
		{
			await SystemPowerService.SetWindowsHibernateEnabledAsync(enabled);
			if (enabled && !SystemPowerService.IsWindowsHibernateAvailable())
			{
				ShowNotification("Hibernation was enabled, but Windows still reports it unavailable on this machine.", isError: true);
				return;
			}

			Log.Information("Windows hibernation {State}", enabled ? "enabled" : "disabled");
			ShowNotification(enabled ? "Hibernation enabled." : "Hibernation disabled.", isError: false);
		});
		OnPropertyChanged(nameof(SleepActions));
	}

	/// <summary>Hides the notification banner.</summary>
	[RelayCommand]
	private void DismissNotification() => NotificationMessage = null;

	/// <summary>Reloads the diagnostic information shown on the system information tab.</summary>
	[RelayCommand]
	private async Task RefreshSystemInfoAsync()
	{
		await RunGuardedAsync(async () =>
		{
			Log.Verbose("Refreshing system information");
			var info = await _systemInfoService.GetSystemInfoAsync();

			SystemInfo.Clear();
			foreach (var item in info)
			{
				SystemInfo.Add(item);
			}

			SystemInfoGroups.Clear();
			foreach (var group in info.GroupBy(item => item.Category))
			{
				SystemInfoGroups.Add(new SystemInfoGroup(group.Key, group.ToList()));
			}
		});
	}

	/// <summary>
	/// Renders the diagnostic information as plain text, so the user can paste it into a bug report.
	/// </summary>
	/// <returns>The facts grouped by category, one "label: value" per line.</returns>
	public string GetSystemInfoAsText() =>
		string.Join(
			Environment.NewLine,
			SystemInfo
				.GroupBy(i => i.Category)
				.Select(g => $"[{g.Key}]{Environment.NewLine}"
					+ string.Join(Environment.NewLine, g.Select(i => $"  {i.Label}: {i.Value}"))));

	/// <summary>
	/// Restarts the application with elevated privileges. On success this call does not return, because
	/// the current process exits as soon as the elevated one has started.
	/// </summary>
	[RelayCommand]
	private void RestartElevated()
	{
		try
		{
			Log.Information("User requested restart with elevated privileges");
			ElevationService.RestartElevated();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Failed to restart elevated");
			ShowNotification(ex.Message, isError: true);
		}
	}

	/// <summary>
	/// Runs an action while showing the busy indicator, turning any failure into a notification.
	/// </summary>
	/// <remarks>
	/// This is the single place where exceptions from the platform services are handled. Catching
	/// everything is deliberate: the boot tools fail in many ways that cannot be anticipated, and the
	/// message they produce is exactly what the user needs to see. Nothing is rethrown, so a failed
	/// operation leaves the application fully usable.
	/// </remarks>
	private async Task RunGuardedAsync(Func<Task> action)
	{
		IsBusy = true;
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Operation failed");
			ShowNotification(ex.Message, isError: true);
		}
		finally
		{
			IsBusy = false;
		}
	}

	/// <summary>Shows a message in the non-modal banner at the bottom of the window.</summary>
	/// <param name="message">Text to display; it stays selectable and copyable in the UI.</param>
	/// <param name="isError">Whether to present it as a failure rather than a confirmation.</param>
	private void ShowNotification(string message, bool isError)
	{
		IsNotificationError = isError;
		NotificationMessage = message;
	}
}
