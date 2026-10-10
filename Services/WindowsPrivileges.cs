using System.Runtime.InteropServices;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Switches on privileges in the access token of the current Windows process.
/// </summary>
/// <remarks>
/// <para>
/// A Windows access token lists the privileges an account holds, but most of them start out
/// <i>disabled</i>. Holding a privilege is therefore not sufficient - it has to be enabled explicitly
/// before an API that requires it will succeed, otherwise the call fails with "access denied". This
/// is the step that is easy to miss, because the account looks perfectly entitled in every UI.
/// </para>
/// <para>
/// A change only affects the current process and is gone when it exits, so nothing has to be undone.
/// </para>
/// </remarks>
internal static class WindowsPrivileges
{
	/// <summary>Permits reading and writing UEFI firmware variables. Held by administrators only.</summary>
	internal const string SeSystemEnvironment = "SeSystemEnvironmentPrivilege";

	/// <summary>
	/// Permits shutting the machine down, which includes suspending and hibernating it. Held by
	/// ordinary users as well, so enabling it does not imply that the process must be elevated.
	/// </summary>
	internal const string SeShutdown = "SeShutdownPrivilege";

	/// <summary>Token attribute flag that switches a privilege on.</summary>
	private const uint SePrivilegeEnabled = 0x00000002;

	/// <summary>Access right needed to change which privileges of a token are enabled.</summary>
	private const uint TokenAdjustPrivileges = 0x0020;

	/// <summary>Access right needed to read a token's contents.</summary>
	private const uint TokenQuery = 0x0008;

	/// <summary>Returns a pseudo handle for the current process; it does not need to be closed.</summary>
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr GetCurrentProcess();

	/// <summary>Opens the access token of a process, which is where its privileges are stored.</summary>
	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

	/// <summary>Translates a privilege name into the locally unique identifier (LUID) the API expects.</summary>
	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool LookupPrivilegeValueW(string? systemName, string name, out long luid);

	/// <summary>
	/// Enables or disables privileges in a token.
	/// </summary>
	/// <remarks>
	/// This function has a well known quirk: it returns <see langword="true"/> even when it could not
	/// grant a requested privilege, reporting <c>ERROR_NOT_ALL_ASSIGNED</c> through the last error code
	/// instead. The last error must therefore be checked explicitly.
	/// </remarks>
	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AdjustTokenPrivileges(
		IntPtr tokenHandle,
		[MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
		ref TokenPrivileges newState,
		uint bufferLength,
		IntPtr previousState,
		IntPtr returnLength);

	/// <summary>Releases a handle obtained from <see cref="OpenProcessToken"/>.</summary>
	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);

	/// <summary>
	/// The Win32 <c>TOKEN_PRIVILEGES</c> structure, narrowed to exactly one privilege.
	/// </summary>
	/// <remarks>
	/// The real structure ends in a variable length array. Declaring a single fixed entry is the
	/// standard simplification and is safe as long as <see cref="PrivilegeCount"/> stays 1.
	/// <c>Pack = 4</c> is required: the native structure is 4 byte aligned, so the 64 bit
	/// <see cref="Luid"/> sits at offset 4. Without it the runtime would align it to offset 8 and the
	/// kernel would read a garbage identifier, which surfaces as <c>ERROR_NOT_ALL_ASSIGNED</c> (1300).
	/// </remarks>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	private struct TokenPrivileges
	{
		/// <summary>Number of privileges that follow. Always 1 here.</summary>
		public uint PrivilegeCount;

		/// <summary>Identifier of the privilege, obtained from <see cref="LookupPrivilegeValueW"/>.</summary>
		public long Luid;

		/// <summary>What to do with it, e.g. <see cref="SePrivilegeEnabled"/>.</summary>
		public uint Attributes;
	}

	/// <summary>
	/// Switches on one privilege of the current process, so that an API which requires it can succeed.
	/// </summary>
	/// <param name="privilegeName">The internal Windows name, e.g. <see cref="SeShutdown"/>.</param>
	/// <exception cref="InvalidOperationException">
	/// The privilege could not be enabled, which means the account does not hold it at all - usually
	/// because the process is not elevated.
	/// </exception>
	internal static void Enable(string privilegeName)
	{
		if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
		{
			throw new InvalidOperationException(
				$"Unable to open the process token to enable {privilegeName} (Win32 error {Marshal.GetLastWin32Error()}).");
		}

		try
		{
			// A null system name means: look the privilege up on this machine.
			if (!LookupPrivilegeValueW(null, privilegeName, out var luid))
			{
				throw new InvalidOperationException(
					$"Unable to look up the {privilegeName} privilege (Win32 error {Marshal.GetLastWin32Error()}).");
			}

			var privileges = new TokenPrivileges
			{
				PrivilegeCount = 1,
				Luid = luid,
				Attributes = SePrivilegeEnabled,
			};

			// AdjustTokenPrivileges reports success even when the privilege was not actually granted,
			// so the last error code has to be inspected as well.
			if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
				|| Marshal.GetLastWin32Error() != 0)
			{
				throw new InvalidOperationException(
					$"Unable to enable the {privilegeName} privilege (Win32 error {Marshal.GetLastWin32Error()}). Administrator rights are required.");
			}

			Log.Verbose("Enabled {Privilege} on the current process token", privilegeName);
		}
		finally
		{
			CloseHandle(token);
		}
	}
}
