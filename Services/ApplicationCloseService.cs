using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// The outcome of asking the running applications to close.
/// </summary>
/// <param name="Asked">How many applications actually received the close request.</param>
/// <param name="StillRunning">
/// Names of the applications that were still alive when the grace period ran out. This is the normal
/// result when an application is showing an "unsaved changes" dialog, not an error.
/// </param>
public sealed record CloseApplicationsResult(int Asked, IReadOnlyList<string> StillRunning)
{
	/// <summary>Whether every application that was asked has ended by now.</summary>
	public bool AllClosed => StillRunning.Count == 0;
}

/// <summary>
/// Asks the applications of the current desktop session to close themselves, the same way the user
/// would by clicking the window's close button.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the user can trigger "close everything" as a deliberate, separate step instead of
/// having to rely on a reboot command to do it. A reboot only ever asks applications on the way down,
/// and on Linux and macOS the ordinary reboot commands do not really ask at all - they signal and then
/// kill. Doing it here means the request is the same action on every platform, it happens while the
/// machine is still up, and the user can see which applications refused before anything restarts.
/// </para>
/// <para>
/// Nothing here ever kills a process. Every application is only <i>asked</i>, so it can save, prompt
/// or refuse; an application that stays open is a legitimate answer and is reported, not overruled.
/// </para>
/// <para>
/// Processes that hold the desktop session itself together are excluded. Closing them would not close
/// an application the user is working in, it would end the session - which is the opposite of giving
/// the user a chance to save. Which names those are cannot be known in advance for every desktop
/// environment, so the list comes from <c>appsettings.json</c> and can be extended by the user.
/// </para>
/// </remarks>
public static class ApplicationCloseService
{
	/// <summary>POSIX signal 15, the polite "please terminate" request. Never signal 9, which cannot be handled.</summary>
	private const int SignalTerminate = 15;

	/// <summary>Signal 0 sends nothing; it only reports whether the process still exists.</summary>
	private const int SignalProbe = 0;

	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

	/// <summary>
	/// The POSIX <c>kill</c> function. Despite the name it only delivers a signal; whether that ends
	/// the process is up to the process. Declared for libc, which exists on both Linux and macOS.
	/// </summary>
	/// <returns>0 when the signal was delivered, -1 on failure (no such process, or not permitted).</returns>
	[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
	private static extern int SendSignal(int processId, int signal);

	/// <summary>The POSIX <c>getuid</c> function, returning the real user id of this process.</summary>
	[DllImport("libc", EntryPoint = "getuid", SetLastError = true)]
	private static extern uint GetUserId();

	/// <summary>
	/// Describes the mechanism used on the current operating system, for the button's tooltip.
	/// </summary>
	/// <remarks>
	/// Derived from the same platform switch the execution path uses, so the tooltip cannot promise
	/// something other than what runs.
	/// </remarks>
	public static string GetMechanism()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "WM_CLOSE to the main window of every application (Win32 CloseMainWindow)";
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return "kill -TERM <pid> for every application of your desktop session";
		}

		return "Not supported on this operating system.";
	}

	/// <summary>
	/// Asks every application of the current desktop session to close, then waits a short while to see
	/// which of them actually ended.
	/// </summary>
	/// <param name="gracePeriod">
	/// How long to wait for the applications to disappear. Defaults to the configured
	/// <see cref="AppSettings.CloseApplicationsGracePeriod"/>. It is not a deadline for the user:
	/// applications left open are only reported.
	/// </param>
	/// <param name="cancellationToken">Stops waiting early; the close requests have already been sent by then.</param>
	/// <returns>How many applications were asked, and which ones were still running afterwards.</returns>
	/// <exception cref="InvalidOperationException">
	/// The list of protected processes is missing from the configuration, or the desktop user could not
	/// be determined on Linux or macOS.
	/// </exception>
	/// <exception cref="PlatformNotSupportedException">The host OS is none of Windows, Linux or macOS.</exception>
	public static async Task<CloseApplicationsResult> CloseAllAsync(
		TimeSpan? gracePeriod = null,
		CancellationToken cancellationToken = default)
	{
		var grace = gracePeriod ?? AppSettings.CloseApplicationsGracePeriod;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return await CloseWindowsApplicationsAsync(grace, cancellationToken).ConfigureAwait(false);
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return await CloseUnixApplicationsAsync(grace, cancellationToken).ConfigureAwait(false);
		}

		throw new PlatformNotSupportedException($"Closing applications is not supported on {RuntimeInformation.OSDescription}.");
	}

	/// <summary>
	/// Windows: posts WM_CLOSE to the main window of every application that has one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WM_CLOSE is literally the message the window's X button sends, which is why an application
	/// responds to it with its usual "save your changes?" dialog and may decide to stay open.
	/// </para>
	/// <para>
	/// A main window handle of zero means there is no window to close - a service, a background task,
	/// or a process in another session - so those are skipped rather than being terminated some other
	/// way. Because this application runs elevated, the message reaches ordinary applications as well:
	/// Windows only blocks messages sent from a lower to a higher integrity level, not the reverse.
	/// </para>
	/// </remarks>
	private static async Task<CloseApplicationsResult> CloseWindowsApplicationsAsync(
		TimeSpan gracePeriod,
		CancellationToken cancellationToken)
	{
		var protectedNames = RequireProtectedNames(
			AppSettings.ProtectedProcessNamesWindows,
			AppSettings.ProtectedProcessNamesWindowsKey);

		var asked = new List<Process>();

		foreach (var process in Process.GetProcesses())
		{
			var requestSent = false;
			try
			{
				if (process.Id != Environment.ProcessId
					&& process.MainWindowHandle != IntPtr.Zero
					&& !IsProtected(process.ProcessName, protectedNames))
				{
					// Returns false when the process has no message loop to receive the request.
					requestSent = process.CloseMainWindow();
					Log.Verbose("Asked {Name} (pid {Id}) to close: {Sent}", process.ProcessName, process.Id, requestSent);
				}
				else
				{
					Log.Verbose("Skipped {Name} (pid {Id}) because it is protected or has no main window", process.ProcessName, process.Id);
				}
			}
			catch (Exception ex)
			{
				// Access denied and "process has exited" are both foreseen while walking the whole list.
				// The id is read separately because reading the name is what typically throws here.
				Log.Warning(ex, "Skipped process {Id} while asking applications to close", GetProcessId(process));
			}

			if (requestSent)
			{
				asked.Add(process);
			}
			else
			{
				process.Dispose();
			}
		}

		return await WaitForWindowsExitAsync(asked, gracePeriod, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<CloseApplicationsResult> WaitForWindowsExitAsync(
		List<Process> asked,
		TimeSpan gracePeriod,
		CancellationToken cancellationToken)
	{
		// One deadline for all of them: waiting on each in turn with its own grace period could add up
		// to minutes when many applications are open.
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(gracePeriod);

		var stillRunning = new List<string>();
		foreach (var process in asked)
		{
			var name = GetProcessName(process);
			try
			{
				await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				stillRunning.Add(name);
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "Could not wait for {Name} to exit", name);
			}

			process.Dispose();
		}

		return new CloseApplicationsResult(asked.Count, Summarize(stillRunning));
	}

	/// <summary>
	/// Linux and macOS: sends SIGTERM to every process of the desktop user.
	/// </summary>
	/// <remarks>
	/// SIGTERM is the signal a program is expected to catch and use to shut down in its own way, so it
	/// is the closest equivalent of the window's close button that works without a window server API.
	/// A program that ignores it simply keeps running and is reported back; SIGKILL, which cannot be
	/// ignored, is deliberately never sent.
	/// </remarks>
	private static async Task<CloseApplicationsResult> CloseUnixApplicationsAsync(
		TimeSpan gracePeriod,
		CancellationToken cancellationToken)
	{
		var desktopUser = GetDesktopUserId();
		var protectedNames = RequireProtectedNames(
			AppSettings.ProtectedProcessNamesUnix,
			AppSettings.ProtectedProcessNamesUnixKey);

		var processes = await ListProcessesAsync(cancellationToken).ConfigureAwait(false);
		var ancestors = GetAncestors(processes);

		var candidates = processes.Where(process =>
			process.UserId == desktopUser
			&& process.Id != Environment.ProcessId
			&& !ancestors.Contains(process.Id)
			&& !IsProtected(process.Name, protectedNames));

		var asked = new List<UnixProcess>();
		foreach (var candidate in candidates)
		{
			if (SendSignal(candidate.Id, SignalTerminate) == 0)
			{
				asked.Add(candidate);
				Log.Verbose("Sent SIGTERM to {Name} (pid {Id})", candidate.Name, candidate.Id);
			}
			else
			{
				// Typically the process ended between listing and signalling.
				Log.Warning("Could not signal {Name} (pid {Id})", candidate.Name, candidate.Id);
			}
		}

		var remaining = asked;
		var deadline = DateTimeOffset.UtcNow + gracePeriod;
		while (remaining.Count > 0 && DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(PollInterval, CancellationToken.None).ConfigureAwait(false);

			// Signal 0 is the standard way to test for a process without disturbing it.
			remaining = remaining.Where(process => SendSignal(process.Id, SignalProbe) == 0).ToList();
		}

		return new CloseApplicationsResult(asked.Count, Summarize(remaining.Select(process => process.Name)));
	}

	/// <summary>
	/// Determines whose applications should be closed on Linux and macOS.
	/// </summary>
	/// <remarks>
	/// The application needs root for the firmware operations and normally relaunches itself through
	/// <c>sudo</c> or <c>pkexec</c>, so its own user id is root's. Root owns system daemons, not the
	/// user's editors and browsers, so the id of the user behind the elevation is used instead. Both
	/// tools pass that id in an environment variable.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Running as root without either variable set. There is no way to tell whose session is meant, and
	/// guessing would mean signalling system daemons.
	/// </exception>
	private static uint GetDesktopUserId()
	{
		if (uint.TryParse(Environment.GetEnvironmentVariable("SUDO_UID"), out var sudoUserId))
		{
			return sudoUserId;
		}

		if (uint.TryParse(Environment.GetEnvironmentVariable("PKEXEC_UID"), out var pkexecUserId))
		{
			return pkexecUserId;
		}

		var userId = GetUserId();
		if (userId == 0)
		{
			throw new InvalidOperationException(
				"Cannot tell which user's applications to close: this process runs as root and neither SUDO_UID nor PKEXEC_UID is set.");
		}

		return userId;
	}

	/// <summary>
	/// Reads the process table through <c>ps</c>.
	/// </summary>
	/// <remarks>
	/// <c>ps -A -o pid=,ppid=,uid=,ucomm=</c>
	/// <list type="bullet">
	/// <item><c>-A</c> lists the processes of all users, not just this terminal's.</item>
	/// <item><c>-o ...</c> selects the columns; the trailing <c>=</c> on each empties its header, so the
	/// output has no header line to skip and every line has the same shape.</item>
	/// <item><c>ucomm</c> is the plain executable name. <c>comm</c> would be a full path on macOS.</item>
	/// </list>
	/// .NET's own <see cref="Process"/> class is not used here because it does not expose the owning
	/// user, which is exactly the field this needs. Note that Linux truncates the name to 15
	/// characters - <see cref="IsProtected"/> accounts for that.
	/// </remarks>
	private static async Task<IReadOnlyList<UnixProcess>> ListProcessesAsync(CancellationToken cancellationToken)
	{
		var result = await ProcessRunner
			.RunAsync("ps", ["-A", "-o", "pid=,ppid=,uid=,ucomm="], cancellationToken)
			.ConfigureAwait(false);

		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				$"Could not list the running processes: ps exited with code {result.ExitCode}. {result.StandardError}".Trim());
		}

		var processes = new List<UnixProcess>();
		foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			// Split into at most four parts so that a name containing spaces ("Google Chrome") stays intact.
			var fields = line.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length == 4
				&& int.TryParse(fields[0], out var id)
				&& int.TryParse(fields[1], out var parentId)
				&& uint.TryParse(fields[2], out var userId))
			{
				processes.Add(new UnixProcess(id, parentId, userId, fields[3].Trim()));
			}
		}

		return processes;
	}

	/// <summary>
	/// Collects this process's parent, its parent's parent and so on.
	/// </summary>
	/// <remarks>
	/// These are the shell, terminal or desktop shell that started the application. They belong to the
	/// same user and would otherwise be asked to close, which would tear down the window this runs in.
	/// </remarks>
	private static HashSet<int> GetAncestors(IReadOnlyList<UnixProcess> processes)
	{
		var parents = new Dictionary<int, int>();
		foreach (var process in processes)
		{
			parents[process.Id] = process.ParentId;
		}

		var ancestors = new HashSet<int>();
		var current = Environment.ProcessId;

		// Adding to the set is also the loop guard: a repeated pid would mean a cycle in the reported table.
		while (parents.TryGetValue(current, out var parent) && parent > 0 && ancestors.Add(parent))
		{
			current = parent;
		}

		return ancestors;
	}

	/// <summary>
	/// Returns the configured protection list, refusing to go on when it is empty.
	/// </summary>
	/// <remarks>
	/// An empty list would mean the desktop shell, the display server and the session bus are asked to
	/// close along with the applications, which ends the session instead of saving the user's work.
	/// Stopping with a message that names the setting is the safe answer.
	/// </remarks>
	private static IReadOnlyList<string> RequireProtectedNames(IReadOnlyList<string> names, string settingKey)
	{
		if (names.Count == 0)
		{
			throw new InvalidOperationException(
				$"No protected processes are configured. Add the list 'BootManager:{settingKey}' to appsettings.json - "
				+ "without it, closing the applications would also close the desktop session itself.");
		}

		return names;
	}

	/// <summary>
	/// Whether a process name is on the given list of processes that must not be asked to close.
	/// </summary>
	/// <remarks>
	/// Linux reports process names truncated to 15 characters, so "gnome-session-binary" arrives as
	/// "gnome-session-b". A name at that length therefore also counts as a match when it is the
	/// beginning of a protected name.
	/// </remarks>
	private static bool IsProtected(string name, IReadOnlyList<string> protectedNames)
	{
		const int linuxNameLimit = 15;

		foreach (var protectedName in protectedNames)
		{
			if (string.Equals(name, protectedName, StringComparison.OrdinalIgnoreCase)
				|| (name.Length >= linuxNameLimit && protectedName.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Reads a process name without throwing for a process that has meanwhile exited.</summary>
	private static string GetProcessName(Process process)
	{
		try
		{
			return process.ProcessName;
		}
		catch (Exception)
		{
			return $"pid {GetProcessId(process)}";
		}
	}

	/// <summary>Reads a process id without throwing, for use in a log message inside a catch block.</summary>
	private static string GetProcessId(Process process)
	{
		try
		{
			return process.Id.ToString();
		}
		catch (Exception)
		{
			return "unknown";
		}
	}

	/// <summary>Turns the raw names into a stable, duplicate-free list fit for showing to the user.</summary>
	private static IReadOnlyList<string> Summarize(IEnumerable<string> names) =>
		names.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();

	/// <summary>One row of the <c>ps</c> output.</summary>
	private sealed record UnixProcess(int Id, int ParentId, uint UserId, string Name);
}
