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
            ElevationMessage = $"Not running as {ElevationService.RequiredPrivilegeName}. Enumerating and changing boot options will likely fail.";
            Log.Verbose("Application is not running with elevated privileges ({Privilege})", ElevationService.RequiredPrivilegeName);
        }

        // Fire and forget: the constructor cannot await, and failures are handled inside the command.
        _ = RefreshCommand.ExecuteAsync(null);
        _ = RefreshSystemInfoCommand.ExecuteAsync(null);
    }

    /// <summary>The boot entries offered by the firmware, in boot order.</summary>
    public ObservableCollection<BootEntry> BootEntries { get; } = [];

    /// <summary>Diagnostic facts about the machine, shown on the system information tab.</summary>
    public ObservableCollection<SystemInfoItem> SystemInfo { get; } = [];

    /// <summary>The entry the user has selected in the list, or null while nothing is selected.</summary>
    [ObservableProperty]
    public partial BootEntry? SelectedEntry { get; set; }

    /// <summary>
    /// Whether the application has the privileges it needs. Defaults to true so the warning banner does
    /// not flash up before the real check has run.
    /// </summary>
    [ObservableProperty]
    public partial bool IsElevated { get; set; } = true;

    /// <summary>Explains the consequences of missing privileges; shown in the warning banner.</summary>
    [ObservableProperty]
    public partial string ElevationMessage { get; set; } = string.Empty;

    /// <summary>Caption of the restart button, which differs per platform.</summary>
    public string RestartElevatedLabel => ElevationService.RestartActionLabel;

    /// <summary>Power actions supported by this OS, shown in the popup menu.</summary>
    public IReadOnlyList<PowerActionKind> PowerActions => SystemPowerService.GetSupportedActions();

    /// <summary>Whether an operation is in progress; drives the progress bar.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The message in the notification banner, or null when nothing is being shown.</summary>
    [ObservableProperty]
    public partial string? NotificationMessage { get; set; }

    /// <summary>Whether the current notification reports a failure, which selects its colour.</summary>
    [ObservableProperty]
    public partial bool IsNotificationError { get; set; }

    /// <summary>Re-reads the boot entries and re-selects the one that will be used for the next boot.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
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
    /// Asks the firmware to show its setup screen on the next boot. Does not restart the machine.
    /// </summary>
    [RelayCommand]
    private async Task ConfigureFirmwareSetupAsync()
    {
        await RunGuardedAsync(async () =>
        {
            await _bootManagerService.RequestBootToFirmwareSetupAsync();
            ShowNotification("System configured to open UEFI firmware setup on next boot.", isError: false);
        });
    }

    /// <summary>Executes a shutdown or reboot action chosen from the power popup menu.</summary>
    public async Task ExecutePowerActionAsync(PowerActionKind action)
    {
        if (action == PowerActionKind.DelayedReboot)
        {
            return;
        }

        await RunGuardedAsync(async () =>
        {
            await SystemPowerService.ExecuteAsync(action).ConfigureAwait(false);
            ShowNotification($"{SystemPowerService.GetLabel(action)} started.", isError: false);
        });
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
