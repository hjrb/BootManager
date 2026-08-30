using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BootManager.Services;
using Serilog;

namespace BootManager.Views;

/// <summary>
/// Countdown window shown before an action that restarts the machine.
/// </summary>
/// <remarks>
/// The delay is the only warning the user gets: the reboot commands used on Linux and macOS do not
/// give applications a chance to prompt about unsaved work. "Close apps" is the active counterpart -
/// it lets the user hand that chance out himself, before anything restarts.
/// </remarks>
public partial class PowerCountdownWindow : Window
{
	private readonly TimeSpan _countdown = AppSettings.PowerCountdown;
	private readonly DispatcherTimer _timer;
	private readonly string _pendingVerb;
	private readonly Func<Task> _action;
	private DateTimeOffset _startedAt;
	private bool _finished;

	/// <summary>Creates the window for the plain delayed reboot action.</summary>
	public PowerCountdownWindow()
		: this(
			"Delayed Reboot",
			"Rebooting",
			SystemPowerService.GetCommandLine(PowerActionKind.ImmediateReboot),
			() => SystemPowerService.ExecuteAsync(PowerActionKind.ImmediateReboot))
	{
	}

	/// <summary>
	/// Creates the window for an arbitrary deferred action.
	/// </summary>
	/// <param name="heading">Window title and headline, e.g. "Restart into UEFI Setup".</param>
	/// <param name="pendingVerb">Verb the countdown line starts with; " in N seconds" is appended.</param>
	/// <param name="commandLine">The command the action runs, shown below the countdown.</param>
	/// <param name="action">Invoked when the countdown elapses or the user presses "Now".</param>
	public PowerCountdownWindow(
		string heading,
		string pendingVerb,
		string commandLine,
		Func<Task> action)
	{
		InitializeComponent();

		_pendingVerb = pendingVerb;
		_action = action;

		Title = heading;
		HeadingText.Text = heading;
		CountdownText.Text = $"{pendingVerb} in {_countdown.TotalSeconds:0} seconds";
		CommandText.Text = $"Runs: {commandLine}";
		CancelButton.Click += (_, _) => Cancel();
		NowButton.Click += (_, _) => _ = RunAsync(_action);
		ToolTip.SetTip(NowButton, $"Skips the rest of the countdown.\n\nRuns: {commandLine}");

		CloseAppsButton.Click += (_, _) => _ = CloseApplicationsAsync();
		ToolTip.SetTip(
			CloseAppsButton,
			"Asks every running application to close, so it can prompt you about unsaved work. "
			+ "Stops the countdown, because those prompts wait for you."
			+ $"\n\nRuns: {ApplicationCloseService.GetMechanism()}");

		// DispatcherTimer ticks on the UI thread, so updating CountdownText.Text here is always safe.
		_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
		_timer.Tick += OnTimerTick;

		_startedAt = DateTimeOffset.UtcNow;
		_timer.Start();
	}

	private void OnTimerTick(object? sender, EventArgs e)
	{
		var remaining = _countdown - (DateTimeOffset.UtcNow - _startedAt);
		if (remaining <= TimeSpan.Zero)
		{
			_ = RunAsync(_action);
			return;
		}

		CountdownText.Text = $"{_pendingVerb} in {Math.Ceiling(remaining.TotalSeconds)} seconds";
	}

	/// <summary>
	/// Asks the running applications to close and reports what came of it.
	/// </summary>
	/// <remarks>
	/// The countdown is stopped for good here. An application that asks about unsaved work is waiting for
	/// the user, and letting the timer fire in the meantime would destroy exactly the work this button
	/// exists to save. From here on the user decides, with "Now" or "Cancel".
	/// </remarks>
	private async Task CloseApplicationsAsync()
	{
		if (_finished)
		{
			return;
		}

		_timer.Stop();
		CountdownText.Text = $"Countdown stopped - {_pendingVerb.ToLowerInvariant()} when you press Now";
		CloseAppsButton.IsEnabled = false;
		StatusText.IsVisible = true;
		StatusText.Text = "Asking the applications to close...";

		try
		{
			var result = await ApplicationCloseService.CloseAllAsync();
			StatusText.Text = Describe(result);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Could not ask the running applications to close");
			StatusText.Text = $"Could not close the applications: {ex.Message}";
		}
		finally
		{
			CloseAppsButton.IsEnabled = true;
		}
	}

	/// <summary>Turns the outcome into one line the user can act on.</summary>
	private static string Describe(CloseApplicationsResult result)
	{
		if (result.Asked == 0)
		{
			return "No open applications found.";
		}

		if (result.AllClosed)
		{
			return $"All {result.Asked} applications closed.";
		}

		// An application that is still there is usually showing a dialog, so it is named rather than counted.
		const int maxNames = 6;
		var names = string.Join(", ", result.StillRunning.Take(maxNames));
		var rest = result.StillRunning.Count - maxNames;

		return rest > 0
			? $"Asked {result.Asked} applications to close. Still open: {names} and {rest} more."
			: $"Asked {result.Asked} applications to close. Still open: {names}.";
	}

	private void Cancel()
	{
		if (_finished)
		{
			return;
		}

		_finished = true;
		_timer.Stop();
		Close();
	}

	private async Task RunAsync(Func<Task> action)
	{
		if (_finished)
		{
			return;
		}

		_finished = true;
		_timer.Stop();
		Close();
		await action().ConfigureAwait(false);
	}
}
