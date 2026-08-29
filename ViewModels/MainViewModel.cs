using System.Collections.ObjectModel;
using BootManager.Models;
using BootManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BootManager.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IBootManagerService _bootManagerService;

    public MainViewModel()
        : this(BootManagerServiceFactory.Create())
    {
    }

    public MainViewModel(IBootManagerService bootManagerService)
    {
        _bootManagerService = bootManagerService;
        _ = RefreshCommand.ExecuteAsync(null);
    }

    public ObservableCollection<BootEntry> BootEntries { get; } = [];

    [ObservableProperty]
    public partial BootEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? NotificationMessage { get; set; }

    [ObservableProperty]
    public partial bool IsNotificationError { get; set; }

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

    [RelayCommand]
    private async Task ConfigureFirmwareSetupAsync()
    {
        await RunGuardedAsync(async () =>
        {
            await _bootManagerService.RequestBootToFirmwareSetupAsync();
            ShowNotification("System configured to open UEFI firmware setup on next boot.", isError: false);
        });
    }

    [RelayCommand]
    private void DismissNotification() => NotificationMessage = null;

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

    private void ShowNotification(string message, bool isError)
    {
        IsNotificationError = isError;
        NotificationMessage = message;
    }
}
