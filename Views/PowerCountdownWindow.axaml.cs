using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BootManager.Services;

namespace BootManager.Views;

/// <summary>
/// Countdown window shown before an action that restarts the machine.
/// </summary>
/// <remarks>
/// The delay is the only warning the user gets: the reboot commands used on Linux and macOS do not
/// give applications a chance to prompt about unsaved work.
/// </remarks>
public partial class PowerCountdownWindow : Window
{
	private readonly TimeSpan _countdown = TimeSpan.FromSeconds(20);
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
			() => SystemPowerService.ExecuteAsync(PowerActionKind.ImmediateReboot),
			SystemPowerService.GetTooltip(PowerActionKind.GracefulReboot),
			() => SystemPowerService.ExecuteAsync(PowerActionKind.GracefulReboot))
	{
	}

	/// <summary>
	/// Creates the window for an arbitrary deferred action.
	/// </summary>
	/// <param name="heading">Window title and headline, e.g. "Restart into UEFI Setup".</param>
	/// <param name="pendingVerb">Verb the countdown line starts with; " in N seconds" is appended.</param>
	/// <param name="commandLine">The command the action runs, shown below the countdown.</param>
	/// <param name="action">Invoked when the countdown elapses or the user presses "Now".</param>
	/// <param name="gracefulTooltip">Tooltip of the "Close apps" button; ignored when <paramref name="gracefulAction"/> is null.</param>
	/// <param name="gracefulAction">
	/// Optional alternative that asks running applications to close first. The button stays hidden when
	/// this is null, which is the case for actions that have no graceful counterpart.
	/// </param>
	public PowerCountdownWindow(
		string heading,
		string pendingVerb,
		string commandLine,
		Func<Task> action,
		string? gracefulTooltip = null,
		Func<Task>? gracefulAction = null)
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

		GracefulButton.IsVisible = gracefulAction is not null;
		if (gracefulAction is not null)
		{
			GracefulButton.Click += (_, _) => _ = RunAsync(gracefulAction);
			ToolTip.SetTip(GracefulButton, gracefulTooltip);
		}

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
