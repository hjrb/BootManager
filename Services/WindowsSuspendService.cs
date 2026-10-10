using System.Runtime.InteropServices;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Puts Windows to sleep through the native power API, without going through hibernation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the usual one-liner.</b> The command normally quoted for this,
/// <c>rundll32.exe powrprof.dll,SetSuspendState 0,1,0</c>, does not do what it looks like: the
/// <c>rundll32</c> entry point ignores the arguments and hibernates the machine whenever hibernation
/// is enabled - the exact opposite of what a "sleep" action promises. Calling
/// <see cref="SetSuspendState"/> directly is the only way to insist on suspend rather than hibernate.
/// </para>
/// <para>
/// <b>What can still write <c>hiberfil.sys</c>.</b> Suspending itself writes no hibernation image, but
/// the separate "Allow hybrid sleep" power setting makes Windows write one anyway while it sleeps, and
/// that setting is on by default on desktop machines. It belongs to the power plan and cannot be
/// overridden by an individual sleep request, so it has to be turned off with <c>powercfg</c> when a
/// sleep that leaves no hibernation image is required.
/// </para>
/// </remarks>
internal static class WindowsSuspendService
{
	/// <summary>Human readable form of the native call, shown in the tooltip of the sleep action.</summary>
	internal const string CommandDescription =
		"powrprof.dll!SetSuspendState(bHibernate: false, bForce: false, bWakeupEventsDisabled: false)";

	/// <summary>
	/// Suspends or hibernates the machine.
	/// </summary>
	/// <remarks>
	/// The call blocks for as long as the machine is asleep and returns once it has woken up again.
	/// It requires <c>SeShutdownPrivilege</c>, which ordinary users hold as well.
	/// </remarks>
	/// <param name="hibernate">
	/// <see langword="true"/> writes the memory image to disk and powers off (S4);
	/// <see langword="false"/> keeps memory powered (S3 or modern standby).
	/// </param>
	/// <param name="force">Documented as having no effect, but still part of the signature.</param>
	/// <param name="wakeupEventsDisabled">
	/// <see langword="true"/> suppresses every wake event, so timers or network activity cannot bring
	/// the machine back.
	/// </param>
	/// <returns><see langword="false"/> on failure; the reason is then in the last Win32 error.</returns>
	[DllImport("powrprof.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.U1)]
	private static extern bool SetSuspendState(
		[MarshalAs(UnmanagedType.U1)] bool hibernate,
		[MarshalAs(UnmanagedType.U1)] bool force,
		[MarshalAs(UnmanagedType.U1)] bool wakeupEventsDisabled);

	/// <summary>
	/// Puts the machine into the sleep state, leaving memory powered.
	/// </summary>
	/// <exception cref="InvalidOperationException">Windows refused to change the power state.</exception>
	internal static void Sleep()
	{
		try
		{
			WindowsPrivileges.Enable(WindowsPrivileges.SeShutdown);
		}
		catch (InvalidOperationException ex)
		{
			// Not fatal: the call usually succeeds anyway, so let it run and report the real error if it
			// does not.
			Log.Warning(ex, "Could not enable {Privilege}; attempting to sleep without it", WindowsPrivileges.SeShutdown);
		}

		// Wake events stay enabled, otherwise a machine set up to wake for maintenance or for
		// Wake-on-LAN would silently stop doing so after this application put it to sleep.
		if (!SetSuspendState(hibernate: false, force: false, wakeupEventsDisabled: false))
		{
			throw new InvalidOperationException(
				$"Windows refused to enter the sleep state (Win32 error {Marshal.GetLastWin32Error()}).");
		}

		Log.Information("Machine resumed from sleep");
	}
}
