using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using BootManager.Models;
using BootManager.Services;
using BootManager.ViewModels;

namespace BootManager.Views;

/// <summary>
/// The application's only window: a tabbed view of the boot entries and the system diagnostics.
/// </summary>
/// <remarks>
/// The two click handlers below live in the view rather than in the view model because the clipboard
/// is reached through the window (<see cref="TopLevel.Clipboard"/>). Keeping that dependency here
/// means the view model stays free of UI framework types and remains testable.
/// </remarks>
public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
	}

	/// <summary>Copies the text of the currently shown notification to the clipboard.</summary>
	private async void OnCopyNotification(object? sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel { NotificationMessage: { Length: > 0 } message })
		{
			await CopyAsync(message);
		}
	}

	/// <summary>Copies the whole system information list as plain text, for pasting into a bug report.</summary>
	private async void OnCopySystemInfo(object? sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel viewModel)
		{
			await CopyAsync(viewModel.GetSystemInfoAsText());
		}
	}

	/// <summary>Copies the command line help, so it can be pasted into a terminal or a note.</summary>
	private async void OnCopyCommandLineHelp(object? sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel viewModel)
		{
			await CopyAsync(viewModel.CommandLineHelpText);
		}
	}

	/// <summary>Shows the About dialog with the version and the licence information.</summary>
	private async void OnAboutButtonClick(object? sender, RoutedEventArgs e) =>
		await new AboutWindow().ShowDialog(this);

	/// <summary>
	/// Opens the README section explaining why removable media are missing from the entry list.
	/// </summary>
	/// <remarks>
	/// Firmware creates boot entries for USB sticks and optical drives during power-on and discards them
	/// again, so the list legitimately differs from one start to the next. That surprises users often
	/// enough to deserve its own button rather than only a tooltip.
	/// </remarks>
	private void OnBootEntryHelpClick(object? sender, RoutedEventArgs e) =>
		AppInfo.OpenInDefaultApplication(AppInfo.GetBootEntryVisibilityHelpTarget());

	/// <summary>
	/// Arms the firmware setup request, behind a countdown on platforms that restart to do so.
	/// </summary>
	private async void OnFirmwareSetupButtonClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel viewModel)
		{
			return;
		}

		if (!viewModel.FirmwareSetupRestartsImmediately)
		{
			await viewModel.ConfigureFirmwareSetupCommand.ExecuteAsync(null);
			return;
		}

		var dialog = new PowerCountdownWindow(
			"Restart into UEFI Setup",
			"Restarting",
			viewModel.FirmwareSetupCommand,
			() => viewModel.ConfigureFirmwareSetupCommand.ExecuteAsync(null));
		await dialog.ShowDialog(this);
	}

	/// <summary>
	/// Writes text to the system clipboard, ignoring the case where no clipboard is available
	/// (which can happen on a headless or unusual desktop session).
	/// </summary>
	private async Task CopyAsync(string text)
	{
		if (Clipboard is { } clipboard)
		{
			await clipboard.SetTextAsync(text);
		}
	}

	/// <summary>Arms an available device on double-click, using the same privilege checks as the toolbar.</summary>
	private async void OnAvailableWakeDeviceDoubleTapped(object? sender, TappedEventArgs e) =>
		await ExecuteWakeDeviceDoubleTapAsync(e, true);

	/// <summary>Disarms an armed device on double-click, using the same privilege checks as the toolbar.</summary>
	private async void OnArmedWakeDeviceDoubleTapped(object? sender, TappedEventArgs e) =>
		await ExecuteWakeDeviceDoubleTapAsync(e, false);

	private async Task ExecuteWakeDeviceDoubleTapAsync(TappedEventArgs e, bool armed)
	{
		if (DataContext is not MainViewModel viewModel || e.Source is not Control source)
		{
			return;
		}
		var item = source as TreeViewItem ?? source.FindAncestorOfType<TreeViewItem>();
		if (item?.DataContext is not WakeDeviceItem device)
		{
			return;
		}
		if (armed)
		{
			viewModel.SelectedAvailableWakeDevice = device;
		}
		else
		{
			viewModel.SelectedArmedWakeDevice = device;
		}
		var command = armed ? viewModel.ArmWakeDeviceCommand : viewModel.DisarmWakeDeviceCommand;
		if (command.CanExecute(null))
		{
			e.Handled = true;
			await command.ExecuteAsync(null);
		}
	}

	/// <summary>Runs the power action of the clicked toolbar button.</summary>
	private async void OnPowerActionClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel viewModel || sender is not Button { DataContext: PowerActionItem action })
		{
			return;
		}

		// The delayed reboot is nothing but the countdown dialog, which reboots once it runs out.
		if (action.Kind == PowerActionKind.DelayedReboot)
		{
			await new PowerCountdownWindow().ShowDialog(this);
			return;
		}

		if (viewModel.IsBusy)
		{
			return;
		}

		if (action.HasHibernateMenu && !SystemPowerService.IsWindowsHibernateAvailable() && viewModel.IsElevated)
		{
			if (await ConfirmHibernateConfigurationAsync(true))
			{
				await viewModel.ConfigureWindowsHibernateAsync(true);
			}
			return;
		}

		await viewModel.ExecutePowerActionAsync(action.Kind);
	}

	/// <summary>Requests disabling Windows hibernation through the Hibernate dropdown.</summary>
	private async void OnDisableHibernateClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel viewModel || viewModel.IsBusy)
		{
			return;
		}

		if (!viewModel.IsElevated)
		{
			await viewModel.ConfigureWindowsHibernateAsync(false);
			return;
		}

		if (await ConfirmHibernateConfigurationAsync(false))
		{
			await viewModel.ConfigureWindowsHibernateAsync(false);
		}
	}

	/// <summary>Confirms a hibernation configuration change without immediately hibernating the machine.</summary>
	private Task<bool> ConfirmHibernateConfigurationAsync(bool enabled)
	{
		var dialog = new Window
		{
			Title = enabled ? "Enable Hibernate?" : "Disable Hibernate?",
			Width = 420,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
		};
		var confirm = new Button { Content = enabled ? "Enable Hibernate" : "Disable Hibernate" };
		var cancel = new Button { Content = "Cancel", IsCancel = true };
		confirm.Click += (_, _) => dialog.Close(true);
		cancel.Click += (_, _) => dialog.Close(false);
		dialog.Content = new StackPanel
		{
			Margin = new Avalonia.Thickness(20),
			Spacing = 16,
			Children =
			{
				new TextBlock
				{
					Text = enabled
						? "Hibernate is disabled or unavailable. Enable it now? This creates a hibernation file on the system drive."
						: "Disable hibernation? This removes the hibernation file and also disables Fast Startup and hybrid sleep.",
					TextWrapping = Avalonia.Media.TextWrapping.Wrap,
				},
				new StackPanel
				{
					Orientation = Avalonia.Layout.Orientation.Horizontal,
					HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
					Spacing = 8,
					Children = { cancel, confirm },
				},
			},
		};
		return dialog.ShowDialog<bool>(this);
	}
}