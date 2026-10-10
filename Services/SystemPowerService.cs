using System.Runtime.InteropServices;
using Serilog;

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

	/// <summary>
	/// Suspend to RAM (ACPI S3, or "modern standby" on newer Windows machines): memory keeps its
	/// contents and stays powered, everything else switches off.
	/// </summary>
	Sleep,

	/// <summary>
	/// Sleep first and hibernate automatically after the delay the OS is configured with, so a machine
	/// left asleep for a long time stops draining its battery.
	/// </summary>
	SuspendThenHibernate,

	/// <summary>
	/// Sleep and write the hibernation image at the same time, so the session also survives losing power.
	/// </summary>
	HybridSleep,

	/// <summary>
	/// Suspend to disk (ACPI S4): the memory contents are written to the hibernation file and the
	/// machine powers off completely.
	/// </summary>
	Hibernate,

	Shutdown,
	FullShutdown,
}

/// <summary>
/// Executes the platform-specific reboot, sleep and shutdown actions used by the application.
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
			// Hybrid sleep and suspend-then-hibernate exist on Windows as well, but only as power plan
			// settings that modify what a plain sleep does - there is no command to request them for a
			// single sleep, so they are not offered as actions.
			return [
				PowerActionKind.GracefulReboot,
				PowerActionKind.ImmediateReboot,
				PowerActionKind.DelayedReboot,
				PowerActionKind.Sleep,
				PowerActionKind.Hibernate,
				PowerActionKind.Shutdown,
				PowerActionKind.FullShutdown,
			];
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			var states = ReadLinuxSleepStates();

			List<PowerActionKind> actions = [
				PowerActionKind.GracefulReboot,
				PowerActionKind.ImmediateReboot,
				PowerActionKind.DelayedReboot,
			];

			if (states.Contains("mem"))
			{
				actions.Add(PowerActionKind.Sleep);
			}

			// All three hibernation based modes need the same thing: a kernel that can write a memory
			// image to disk. Without it systemd would reject every one of them.
			if (states.Contains("disk"))
			{
				actions.Add(PowerActionKind.SuspendThenHibernate);
				actions.Add(PowerActionKind.HybridSleep);
				actions.Add(PowerActionKind.Hibernate);
			}

			actions.Add(PowerActionKind.Shutdown);
			return actions;
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			// macOS has no user-facing hibernate command: what the hardware does when it sleeps is
			// decided by the "hibernatemode" power setting, not by the request itself.
			return [
				PowerActionKind.GracefulReboot,
				PowerActionKind.ImmediateReboot,
				PowerActionKind.DelayedReboot,
				PowerActionKind.Sleep,
				PowerActionKind.Shutdown,
			];
		}

		return [];
	}

	/// <summary>
	/// Reads the sleep states the running Linux kernel offers.
	/// </summary>
	/// <remarks>
	/// <c>/sys/power/state</c> holds the supported states separated by spaces, for example
	/// <c>freeze mem disk</c>: <c>mem</c> is suspend to RAM and <c>disk</c> is hibernation, which the
	/// kernel only lists when it was built with hibernation support. Asking the kernel is the only way
	/// to tell beforehand whether a machine can hibernate at all - many cannot, because they have no
	/// swap area to write the image to.
	/// </remarks>
	/// <returns>
	/// The state names, or all of them when the file cannot be read, so that an unreadable file hides
	/// nothing and the action simply fails later with the system's own error message.
	/// </returns>
	private static string[] ReadLinuxSleepStates()
	{
		try
		{
			var states = File.ReadAllText("/sys/power/state")
				.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			Log.Verbose("Kernel reports the sleep states {States}", states);
			return states;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Log.Information(ex, "Could not read /sys/power/state; offering every sleep action");
			return ["freeze", "mem", "disk"];
		}
	}

	/// <summary>
	/// Returns the UI label for a given power action.
	/// </summary>
	public static string GetLabel(PowerActionKind action) => action switch
	{
		PowerActionKind.GracefulReboot => "Reboot now (graceful)",
		PowerActionKind.ImmediateReboot => "Reboot now (forced)",
		PowerActionKind.DelayedReboot => $"Delayed reboot ({AppSettings.PowerCountdown.TotalSeconds:0}s)",
		PowerActionKind.Sleep => "Sleep",
		PowerActionKind.SuspendThenHibernate => "Sleep, then hibernate",
		PowerActionKind.HybridSleep => "Hybrid sleep",
		PowerActionKind.Hibernate => "Hibernate",
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
			$"Shows a {AppSettings.PowerCountdown.TotalSeconds:0} second countdown so you can save your work, then reboots.",
		PowerActionKind.Sleep =>
			"Keeps the session in memory and powers the rest of the machine down, so it resumes within seconds. "
			+ "The session is lost if the machine loses power. Whether a hibernation image is written as well is "
			+ "an operating system power setting (hybrid sleep on Windows, hibernatemode on macOS), not part of this request.",
		PowerActionKind.SuspendThenHibernate =>
			"Sleeps now and hibernates by itself once the delay configured in the system has passed, so a machine "
			+ "left asleep stops draining its battery without losing the session.",
		PowerActionKind.HybridSleep =>
			"Sleeps and writes the hibernation image at the same time: it resumes from memory as usual, and still "
			+ "comes back from disk if the machine loses power while asleep.",
		PowerActionKind.Hibernate =>
			"Writes the whole session to the hibernation file and powers the machine off completely. Resuming is "
			+ "slower than from sleep, but no power is needed in between. Note that the disks are left in a "
			+ "hibernated state that another operating system must not write to.",
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
	/// <para>
	/// <b>Sleep is the one action that is not a command on every platform.</b> On Windows it is a direct
	/// call into <c>powrprof.dll</c>, because the equivalent <c>rundll32</c> command silently hibernates
	/// instead of sleeping whenever hibernation is enabled; see <see cref="WindowsSuspendService"/>.
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

			case PowerActionKind.Sleep:
				if (windows)
				{
					return PowerCommand.Native(
						WindowsSuspendService.CommandDescription,
						// The call blocks until the machine wakes up again, so it must not run on the UI thread.
						() => Task.Run(WindowsSuspendService.Sleep));
				}

				// systemd picks "mem" (suspend to RAM) and writes nothing to disk.
				if (linux) return new PowerCommand("systemctl", ["suspend"]);

				// pmset sleepnow is the scripted equivalent of the Apple menu's "Sleep". How deeply the
				// machine then sleeps is governed by the "hibernatemode" setting, which this does not change.
				if (mac) return new PowerCommand("pmset", ["sleepnow"]);
				break;

			case PowerActionKind.SuspendThenHibernate:
				// Linux only: the delay comes from HibernateDelaySec in /etc/systemd/sleep.conf. Windows
				// and macOS have the same behaviour, but only as a power setting, not as a request.
				if (linux) return new PowerCommand("systemctl", ["suspend-then-hibernate"]);
				throw new NotSupportedException(
					"This operating system cannot be asked for a suspend-then-hibernate; it is a power setting there.");

			case PowerActionKind.HybridSleep:
				// Linux only, for the same reason: on Windows this is the "Allow hybrid sleep" power plan
				// option, which changes what a plain sleep does rather than being requestable on its own.
				if (linux) return new PowerCommand("systemctl", ["hybrid-sleep"]);
				throw new NotSupportedException(
					"This operating system cannot be asked for a hybrid sleep; it is a power setting there.");

			case PowerActionKind.Hibernate:
				// shutdown.exe /h hibernates. It takes no /t, because hibernation starts immediately, and
				// it fails with a message of its own when hibernation is switched off (powercfg /h off).
				if (windows) return new PowerCommand("shutdown.exe", ["/h"]);
				if (linux) return new PowerCommand("systemctl", ["hibernate"]);

				// macOS has no hibernate command: "hibernatemode" decides what a sleep turns into.
				throw new NotSupportedException("This operating system does not expose a hibernate action.");

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
	/// Triggers a reboot, sleep or shutdown action on the machine.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The delayed reboot action is performed in the UI with a countdown window that then calls this
	/// method with <see cref="PowerActionKind.ImmediateReboot"/> or
	/// <see cref="PowerActionKind.GracefulReboot"/>, so no countdown logic lives here.
	/// </para>
	/// <para>
	/// The sleep actions only return once the machine has woken up again, because both the native
	/// Windows call and <c>systemctl suspend</c> block for as long as the machine is asleep.
	/// </para>
	/// </remarks>
	public static async Task ExecuteAsync(PowerActionKind action, CancellationToken cancellationToken = default)
	{
		var command = ResolveCommand(action);

		if (command.NativeCall is { } nativeCall)
		{
			await nativeCall().ConfigureAwait(false);
			return;
		}

		var result = await ProcessRunner.RunAsync(command.FileName, command.Arguments, cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				$"{command.FileName} failed with exit code {result.ExitCode}: {result.StandardError}");
		}
	}
}

/// <summary>
/// How a power action is carried out on the current operating system.
/// </summary>
/// <param name="FileName">
/// The program to start, or - for a <see cref="Native"/> command - the API call written out for display.
/// </param>
/// <param name="Arguments">One element per argument, passed to the program without shell interpretation.</param>
public sealed record PowerCommand(string FileName, IReadOnlyList<string> Arguments)
{
	/// <summary>
	/// The operating system API call that performs this action, or <see langword="null"/> when the
	/// action is performed by starting <see cref="FileName"/> as a program.
	/// </summary>
	public Func<Task>? NativeCall { get; private init; }

	/// <summary>
	/// Builds a command that is carried out by calling an OS API directly instead of running a program.
	/// </summary>
	/// <param name="signature">The call written out, so tooltips can show it like any other command.</param>
	/// <param name="call">Performs the call; it may block for a long time and must not run on the UI thread.</param>
	public static PowerCommand Native(string signature, Func<Task> call) =>
		new(signature, []) { NativeCall = call };

	/// <summary>Renders the command the way it would be typed into a terminal.</summary>
	public override string ToString() =>
		Arguments.Count == 0 ? FileName : $"{FileName} {string.Join(' ', Arguments.Select(Quote))}";

	private static string Quote(string argument) =>
		argument.Contains(' ') ? $"'{argument}'" : argument;
}
