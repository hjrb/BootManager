using System.Runtime.InteropServices;

namespace BootManager.Services;

/// <summary>
/// Cross-platform actions that can be triggered from the main window's toolbar.
/// </summary>
/// <remarks>
/// The toolbar intentionally only exposes actions that the host operating system supports. A forced
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
	Sleep,
	Hibernate,
	Shutdown,
	FullShutdown,

	/// <summary>Signs out the current desktop session without rebooting the computer.</summary>
	Logoff,
}

/// <summary>The toolbar section a power action belongs to.</summary>
public enum PowerActionCategory
{
	Reboot,
	Sleep,
	Shutdown,

	/// <summary>Actions that end the desktop session without powering off.</summary>
	Logoff,
}

/// <summary>
/// Executes the platform-specific reboot and shutdown actions used by the application.
/// </summary>
public static class SystemPowerService
{
	/// <summary>Reads Windows power capabilities without elevation or changing power settings; zero indicates failure.</summary>
	[DllImport("powrprof.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.U1)]
	private static extern bool GetPwrCapabilities(out WindowsPowerCapabilities capabilities);

	/// <summary>Reserves the full 76-byte Win32 SYSTEM_POWER_CAPABILITIES buffer, exposing only hibernation flags.</summary>
	/// <remarks>Native BOOLEAN fields occupy one byte. S4 support and a hibernation file must both be present.</remarks>
	[StructLayout(LayoutKind.Explicit, Size = 76)]
	private struct WindowsPowerCapabilities
	{
		/// <summary>Whether Windows currently supports the S4 hibernation state.</summary>
		[FieldOffset(6)]
		public byte SystemS4;

		/// <summary>Whether Windows has a hibernation file available.</summary>
		[FieldOffset(8)]
		public byte HibernationFilePresent;
	}

	/// <summary>
	/// Returns the supported actions for the current OS, in the order they should be shown in the UI.
	/// </summary>
	/// <remarks>Windows Hibernate stays visible even when unavailable, so the UI can offer to enable it.</remarks>
	public static IReadOnlyList<PowerActionKind> GetSupportedActions()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return [
				PowerActionKind.GracefulReboot,
				PowerActionKind.ImmediateReboot,
				PowerActionKind.DelayedReboot,
				PowerActionKind.Sleep,
				PowerActionKind.Hibernate,
				PowerActionKind.Shutdown,
				PowerActionKind.FullShutdown,
				PowerActionKind.Logoff,
			];
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return [
				PowerActionKind.GracefulReboot,
				PowerActionKind.ImmediateReboot,
				PowerActionKind.DelayedReboot,
				PowerActionKind.Sleep,
				PowerActionKind.Hibernate,
				PowerActionKind.Shutdown,
				.. string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XDG_SESSION_ID"))
					? Array.Empty<PowerActionKind>() : new[] { PowerActionKind.Logoff },
			];
		}

		// macOS has no hibernate of its own: it is a mode of sleep there, chosen by the power settings.
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return [
				PowerActionKind.GracefulReboot,
				PowerActionKind.ImmediateReboot,
				PowerActionKind.DelayedReboot,
				PowerActionKind.Sleep,
				PowerActionKind.Shutdown,
				PowerActionKind.Logoff,
			];
		}

		return [];
	}

	/// <summary>Checks Windows hibernation availability without requiring administrator rights.</summary>
	public static bool IsWindowsHibernateAvailable() =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		&& GetPwrCapabilities(out var capabilities)
		&& capabilities.SystemS4 != 0 && capabilities.HibernationFilePresent != 0;

	/// <summary>Changes Windows hibernation configuration, without entering hibernation.</summary>
	/// <param name="enabled">Whether to enable hibernation; disabling also disables Fast Startup and hybrid sleep.</param>
	/// <remarks>Requires elevation. powercfg /hibernate on enables hibernation; /type full supports full sessions; off removes the file.</remarks>
	public static async Task SetWindowsHibernateEnabledAsync(bool enabled)
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			throw new PlatformNotSupportedException("Hibernation configuration is supported only on Windows.");
		}

		if (!ElevationService.IsElevated())
		{
			throw new InvalidOperationException("Changing hibernation requires administrator rights.");
		}

		var result = await ProcessRunner.RunAsync("powercfg.exe", ["/hibernate", enabled ? "on" : "off"]).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException($"Changing hibernation failed (exit code {result.ExitCode}): {result.StandardError}");
		}

		if (enabled)
		{
			var fullFileResult = await ProcessRunner.RunAsync("powercfg.exe", ["/hibernate", "/type", "full"]).ConfigureAwait(false);
			if (!fullFileResult.Succeeded)
			{
				throw new InvalidOperationException($"Creating a full hibernation file failed (exit code {fullFileResult.ExitCode}): {fullFileResult.StandardError}");
			}
		}
	}

	/// <summary>
	/// Returns the toolbar section an action belongs to, so the UI can group the buttons.
	/// </summary>
	public static PowerActionCategory GetCategory(PowerActionKind action) => action switch
	{
		PowerActionKind.Sleep or PowerActionKind.Hibernate => PowerActionCategory.Sleep,
		PowerActionKind.Shutdown or PowerActionKind.FullShutdown => PowerActionCategory.Shutdown,
		PowerActionKind.Logoff => PowerActionCategory.Logoff,
		_ => PowerActionCategory.Reboot,
	};

	/// <summary>
	/// Returns the toolbar button caption for a power action. It is deliberately short, because the
	/// section heading above the button already names the kind of action.
	/// </summary>
	public static string GetLabel(PowerActionKind action) => action switch
	{
		PowerActionKind.GracefulReboot => "Graceful",
		PowerActionKind.ImmediateReboot => "Forced",
		PowerActionKind.DelayedReboot => $"Delayed ({AppSettings.PowerCountdown.TotalSeconds:0}s)",
		PowerActionKind.Sleep => "Sleep",
		PowerActionKind.Hibernate => "Hibernate",
		// Windows always shuts down into the hybrid boot cache unless the full variant is used.
		PowerActionKind.Shutdown => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Hybrid" : "Shutdown",
		PowerActionKind.FullShutdown => "Full",
		PowerActionKind.Logoff => "Logoff",
		_ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
	};

	/// <summary>
	/// Returns the self-contained name of a power action, for messages that have no section heading
	/// to lean on.
	/// </summary>
	public static string GetFullName(PowerActionKind action) => action switch
	{
		PowerActionKind.GracefulReboot => "Graceful reboot",
		PowerActionKind.ImmediateReboot => "Forced reboot",
		PowerActionKind.DelayedReboot => "Delayed reboot",
		PowerActionKind.Sleep => "Sleep",
		PowerActionKind.Hibernate => "Hibernate",
		PowerActionKind.Shutdown => "Shutdown",
		PowerActionKind.FullShutdown => "Full shutdown",
		PowerActionKind.Logoff => "Logoff",
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
			$"Shows a {AppSettings.PowerCountdown.TotalSeconds:0} second countdown so you can save your work, then reboots.",
		PowerActionKind.Sleep =>
			"Suspends the machine to memory, keeping the session open.",
		PowerActionKind.Hibernate =>
			"Writes memory to disk and powers off, restoring the session on the next start. Like Fast Startup, this leaves file systems in a state another operating system must not write to.",
		PowerActionKind.Shutdown => "Powers the machine off.",
		PowerActionKind.FullShutdown => "Powers the machine off without the hybrid boot cache, for a true cold start.",
		PowerActionKind.Logoff => "Signs out the current desktop session. Save your work first; on Linux applications are terminated without prompting.",
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
		if (action == PowerActionKind.Sleep && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "powrprof.dll!SetSuspendState(false, false, false) (native Windows API)";
		}

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

			case PowerActionKind.Sleep:
				if (windows) throw new NotSupportedException("Windows sleep is handled by the native SetSuspendState API, not a command.");
				if (linux) return new PowerCommand("systemctl", ["suspend"]);
				if (mac) return new PowerCommand("pmset", ["sleepnow"]);
				break;

			case PowerActionKind.Hibernate:
				if (windows) return new PowerCommand("shutdown.exe", ["/h"]);
				if (linux) return new PowerCommand("systemctl", ["hibernate"]);
				throw new NotSupportedException("macOS has no separate hibernate action; its sleep modes are chosen in the power settings.");

			case PowerActionKind.FullShutdown:
				if (windows) return new PowerCommand("shutdown.exe", ["/s", "/f", "/t", "0"]);
				throw new NotSupportedException("This operating system does not support a separate full shutdown action.");

			case PowerActionKind.Logoff:
				// /l signs out the caller's Windows session; omitting /f allows unsaved-work prompts.
				if (windows) return new PowerCommand("shutdown.exe", ["/l"]);
				if (linux)
				{
					var sessionId = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
					if (string.IsNullOrWhiteSpace(sessionId))
					{
						throw new NotSupportedException("The current desktop session could not be determined for logoff.");
					}
					// terminate-session ends only the specified login session, not every session of the user.
					return new PowerCommand("loginctl", ["terminate-session", sessionId]);
				}
				// The System Events logout request lets macOS applications prompt about unsaved work.
				if (mac) return new PowerCommand("osascript", ["-e", "tell application \"System Events\" to log out"]);
				break;

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
		if (action == PowerActionKind.Sleep && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			await Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				WindowsFirmwareVariables.EnableShutdownPrivilege();
				if (!SetSuspendState(false, false, false))
				{
					throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enter sleep.");
				}
			}, cancellationToken).ConfigureAwait(false);
			return;
		}

		var command = ResolveCommand(action);
		var result = await ProcessRunner.RunAsync(command.FileName, command.Arguments, cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				$"{command.FileName} failed with exit code {result.ExitCode}: {result.StandardError}");
		}
	}

	/// <summary>Suspends Windows with one-byte BOOLEAN arguments, unlike the incompatible rundll32 callback signature.</summary>
	/// <remarks>
	/// Hibernate false selects sleep; force is unused; wakeupEventsDisabled false preserves wake events.
	/// Requires SeShutdownPrivilege. Zero indicates failure and exposes a Win32 last-error code.
	/// </remarks>
	[DllImport("powrprof.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.U1)]
	private static extern bool SetSuspendState(
		[MarshalAs(UnmanagedType.U1)] bool hibernate,
		[MarshalAs(UnmanagedType.U1)] bool force,
		[MarshalAs(UnmanagedType.U1)] bool wakeupEventsDisabled);
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
