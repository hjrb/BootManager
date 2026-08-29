using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Turns off Windows "Fast Startup" (also called <i>hiberboot</i> or <i>hybrid shutdown</i>).
/// </summary>
/// <remarks>
/// <para>
/// With Fast Startup enabled, shutting Windows down does not really shut it down: the kernel session
/// is hibernated to <c>hiberfil.sys</c> and restored on the next start. The file systems are therefore
/// still considered "in use" by Windows, and another operating system that writes to them can corrupt
/// them. That makes this setting one of the most common causes of dual-boot trouble, which is why the
/// application offers a way to switch it off.
/// </para>
/// <para>
/// The setting is stored as the <c>HiberbootEnabled</c> registry value, which is exactly what the
/// "Turn on fast startup" checkbox in the Control Panel writes. Changing the registry directly is
/// preferred over <c>powercfg /h off</c>, because the latter disables hibernation as a whole and would
/// also remove the machine's ability to hibernate and to use the fast-resume feature at all.
/// </para>
/// <para>
/// Writing to <c>HKEY_LOCAL_MACHINE</c> requires Administrator rights; without them the registry key
/// cannot be opened for writing. The change takes effect on the next shutdown.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsFastStartupService
{
	/// <summary>Registry key holding the power settings, including the Fast Startup flag.</summary>
	private const string PowerKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

	/// <summary>Name of the value that switches Fast Startup on (1) or off (0).</summary>
	private const string HiberbootValueName = "HiberbootEnabled";

	/// <summary>
	/// Disables Fast Startup so that a shutdown really powers the machine down.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when the setting was changed, <see langword="false"/> when Fast Startup
	/// was already off and nothing had to be written.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The registry key could not be opened for writing or the value could not be set, which almost
	/// always means the process is not running as Administrator.
	/// </exception>
	internal static bool Disable()
	{
		// writable: true asks for write access up front, so a missing privilege fails here rather than
		// half way through the change.
		using var key = Registry.LocalMachine.OpenSubKey(PowerKeyPath, writable: true)
			?? throw new InvalidOperationException(
				$@"The registry key 'HKEY_LOCAL_MACHINE\{PowerKeyPath}' could not be opened for writing. "
				+ "Administrator privileges are required to change the Fast Startup setting.");

		if (key.GetValue(HiberbootValueName) as int? == 0)
		{
			Log.Verbose("Fast Startup is already disabled; leaving the registry untouched");
			return false;
		}

		try
		{
			key.SetValue(HiberbootValueName, 0, RegistryValueKind.DWord);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
		{
			throw new InvalidOperationException(
				"Fast Startup could not be disabled because the registry value could not be written. "
				+ "Administrator privileges are required.",
				ex);
		}

		Log.Information("Windows Fast Startup disabled; the change takes effect on the next shutdown");
		return true;
	}
}
