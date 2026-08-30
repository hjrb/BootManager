using System.Runtime.InteropServices;

namespace BootManager.Services;

/// <summary>
/// Cross-platform actions that can be triggered from the main window's power menu.
/// </summary>
/// <remarks>
/// The menu intentionally only exposes actions that the host operating system supports. A forced
/// shutdown exists on Windows, but not in a portable cross-platform form, so Linux and macOS hide it
/// even though they support a normal shutdown.
/// </remarks>
public enum PowerActionKind
{
	/// <summary>Reboot through the OS mechanism that lets applications object; it may end up not rebooting.</summary>
	GracefulReboot,

	/// <summary>Reboot right now, killing applications without asking. Unsaved work is lost.</summary>
	ImmediateReboot,

	DelayedReboot,
	Shutdown,
	FullShutdown,
}

/// <summary>
/// Executes the platform-specific reboot and shutdown actions used by the application.
/// </summary>
public static class SystemPowerService
{
	/// <summary>
	/// Returns the supported actions for the current OS, in the order they should be shown in the UI.
	/// </summary>
	public static IReadOnlyList<PowerActionKind> GetSupportedActions()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return [
				PowerActionKind.GracefulReboot,
				PowerActionKind.ImmediateReboot,
				PowerActionKind.DelayedReboot,
				PowerActionKind.Shutdown,
				PowerActionKind.FullShutdown,
			];
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return [
				PowerActionKind.GracefulReboot,
				PowerActionKind.ImmediateReboot,
				PowerActionKind.DelayedReboot,
				PowerActionKind.Shutdown,
			];
		}

		return [];
	}

	/// <summary>
	/// Returns the UI label for a given power action.
	/// </summary>
	public static string GetLabel(PowerActionKind action) => action switch
	{
		PowerActionKind.GracefulReboot => "Reboot now (graceful)",
		PowerActionKind.ImmediateReboot => "Reboot now (forced)",
		PowerActionKind.DelayedReboot => "Delayed reboot (20s)",
		PowerActionKind.Shutdown => "Shutdown",
		PowerActionKind.FullShutdown => "Full shutdown",
		_ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
	};

	/// <summary>
	/// Returns a longer explanation of a power action, shown as a tooltip.
	/// </summary>
	public static string GetDescription(PowerActionKind action) => action switch
	{
		PowerActionKind.GracefulReboot =>
			"Asks running applications to close first, so they can prompt about unsaved work. An application may cancel the reboot.",
		PowerActionKind.ImmediateReboot =>
			"Reboots straight away and terminates applications without warning. Unsaved work is lost.",
		PowerActionKind.DelayedReboot =>
			"Shows a 20 second countdown so you can save your work, then reboots.",
		PowerActionKind.Shutdown => "Powers the machine off.",
		PowerActionKind.FullShutdown => "Powers the machine off without the hybrid boot cache, for a true cold start.",
		_ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
	};

	/// <summary>
	/// Returns the tooltip for a power action: the explanation followed by the command it runs.
	/// </summary>
	public static string GetTooltip(PowerActionKind action) =>
		$"{GetDescription(action)}\n\nRuns: {GetCommandLine(action)}";

	/// <summary>
	/// Returns the exact command a power action runs on the current OS, formatted for display.
	/// </summary>
	/// <remarks>
	/// Shown in the tooltips so the user can see what the application is about to do, rather than
	/// having to trust a label. It is derived from the same <see cref="ResolveCommand"/> the execution
	/// path uses, so it cannot drift out of sync with what actually runs.
	/// </remarks>
	public static string GetCommandLine(PowerActionKind action)
	{
		if (action == PowerActionKind.DelayedReboot)
		{
			return $"After the countdown: {GetCommandLine(PowerActionKind.ImmediateReboot)}";
		}

		try
		{
			return ResolveCommand(action).ToString();
		}
		catch (Exception ex) when (ex is PlatformNotSupportedException or NotSupportedException)
		{
			return "Not supported on this operating system.";
		}
	}

	/// <summary>
	/// Maps a power action to the executable and arguments that carry it out on the current OS.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Graceful versus forced.</b> Only the graceful variant routes through the mechanism that lets
	/// applications ask the user about unsaved work - and therefore refuse. Because the three platforms
	/// expose that through completely different channels (a non-forced <c>shutdown.exe</c>, systemd
	/// inhibitor locks, an AppleEvent to loginwindow), the two variants do not differ by a single flag.
	/// </para>
	/// <para>
	/// The Windows "full shutdown" action is the closest equivalent to a true full power-off rather
	/// than the hybrid shutdown that modern Windows does. Linux and macOS expose no separate
	/// full-shutdown mode that is portable, so the action is hidden there.
	/// </para>
	/// </remarks>
	/// <exception cref="NotSupportedException">The action has no command of its own, or the OS lacks it.</exception>
	/// <exception cref="PlatformNotSupportedException">The host OS is none of Windows, Linux or macOS.</exception>
	public static PowerCommand ResolveCommand(PowerActionKind action)
	{
		var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
		var linux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
		var mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

		switch (action)
		{
			case PowerActionKind.DelayedReboot:
				throw new NotSupportedException("Delayed reboot is handled by the countdown dialog and has no command of its own.");

			case PowerActionKind.GracefulReboot:
				// Windows: /t 0 starts immediately; omitting /f is what makes it graceful, because a
				// timeout greater than zero would imply /f and close applications without warning.
				if (windows) return new PowerCommand("shutdown.exe", ["/r", "/t", "0"]);

				// Linux: --check-inhibitors=yes honors block inhibitor locks, so a busy application (a
				// package manager, a running backup) can refuse the reboot. Without it, root bypasses them.
				if (linux) return new PowerCommand("systemctl", ["reboot", "--check-inhibitors=yes"]);

				// macOS: the AppleEvent route asks every application to quit, so they can show their
				// "unsaved changes" dialogs and cancel. "shutdown -r" would bypass all of that.
				if (mac) return new PowerCommand("osascript", ["-e", "tell application \"System Events\" to restart"]);
				break;

			case PowerActionKind.ImmediateReboot:
				if (windows) return new PowerCommand("shutdown.exe", ["/r", "/t", "0", "/f"]);

				// A single --force kills every process but still unmounts file systems. A second one
				// would skip unmounting too, which risks data loss, so it is deliberately not used.
				if (linux) return new PowerCommand("systemctl", ["reboot", "--force", "--no-wall"]);
				if (mac) return new PowerCommand("shutdown", ["-r", "now"]);
				break;

			case PowerActionKind.Shutdown:
				if (windows) return new PowerCommand("shutdown.exe", ["/s", "/t", "0", "/f"]);
				if (linux) return new PowerCommand("systemctl", ["poweroff"]);
				if (mac) return new PowerCommand("shutdown", ["-h", "now"]);
				break;

			case PowerActionKind.FullShutdown:
				if (windows) return new PowerCommand("shutdown.exe", ["/s", "/f", "/t", "0"]);
				throw new NotSupportedException("This operating system does not support a separate full shutdown action.");

			default:
				throw new ArgumentOutOfRangeException(nameof(action), action, null);
		}

		throw new PlatformNotSupportedException($"Power actions are not supported on {RuntimeInformation.OSDescription}.");
	}

	/// <summary>
	/// Triggers a reboot or shutdown action on the machine.
	/// </summary>
	/// <remarks>
	/// The delayed reboot action is performed in the UI with a countdown window that then calls this
	/// method with <see cref="PowerActionKind.ImmediateReboot"/> or
	/// <see cref="PowerActionKind.GracefulReboot"/>, so no countdown logic lives here.
	/// </remarks>
	public static async Task ExecuteAsync(PowerActionKind action, CancellationToken cancellationToken = default)
	{
		var command = ResolveCommand(action);
		var result = await ProcessRunner.RunAsync(command.FileName, command.Arguments, cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				$"{command.FileName} failed with exit code {result.ExitCode}: {result.StandardError}");
		}
	}
}

/// <summary>
/// The executable and arguments that carry out a power action on the current operating system.
/// </summary>
/// <param name="FileName">The program to start.</param>
/// <param name="Arguments">One element per argument, passed to the program without shell interpretation.</param>
public sealed record PowerCommand(string FileName, IReadOnlyList<string> Arguments)
{
	/// <summary>Renders the command the way it would be typed into a terminal.</summary>
	public override string ToString() =>
		Arguments.Count == 0 ? FileName : $"{FileName} {string.Join(' ', Arguments.Select(Quote))}";

	private static string Quote(string argument) =>
		argument.Contains(' ') ? $"'{argument}'" : argument;
}
