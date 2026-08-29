using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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

	/// <summary>Shows the power menu containing only the actions supported by the current OS.</summary>
	private async void OnPowerButtonClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel viewModel)
		{
			return;
		}

		var actions = viewModel.PowerActions;
		if (actions.Count == 0)
		{
			return;
		}

		var menu = new ContextMenu();
		foreach (var action in actions)
		{
			var item = new MenuItem
			{
				Header = SystemPowerService.GetLabel(action),
			};

			item.Click += async (_, _) =>
			{
				if (action == PowerActionKind.DelayedReboot)
				{
					var dialog = new PowerCountdownWindow();
					await dialog.ShowDialog(this);
					return;
				}

				await viewModel.ExecutePowerActionAsync(action);
			};

			menu.Items.Add(item);
		}

		menu.Open(PowerButton);
	}
}