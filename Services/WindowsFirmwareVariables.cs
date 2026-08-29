using System.Runtime.InteropServices;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Reads and writes UEFI firmware variables on Windows through the Win32 API.
/// </summary>
/// <remarks>
/// <para>
/// UEFI firmware keeps its settings in named variables in NVRAM, each belonging to a namespace
/// identified by a GUID. This class only deals with the standard "EFI global" namespace, which is
/// where the variables defined by the UEFI specification live.
/// </para>
/// <para>
/// <b>Two privilege layers apply.</b> The process must be elevated, and on top of that it must
/// explicitly switch on the <c>SeSystemEnvironmentPrivilege</c> in its access token. Windows grants
/// administrators that privilege but leaves it disabled by default, so the firmware calls fail with
/// "access denied" until <see cref="EnableFirmwarePrivilege"/> has been called.
/// </para>
/// <para>
/// Reading also fails on machines that were booted in legacy BIOS/CSM mode, because there is no UEFI
/// variable store in that case.
/// </para>
/// </remarks>
internal static class WindowsFirmwareVariables
{
    /// <summary>
    /// GUID of the "EFI global variable" namespace defined by the UEFI specification. Variables such
    /// as <c>BootOrder</c>, <c>BootNext</c> and <c>OsIndications</c> all live here.
    /// </summary>
    private const string EfiGlobalVariableGuid = "{8be4df61-93ca-11d2-aa0d-00e098032b8c}";

    /// <summary>Windows' internal name for the privilege that permits access to firmware variables.</summary>
    private const string SeSystemEnvironmentName = "SeSystemEnvironmentPrivilege";

    /// <summary>Token attribute flag that switches a privilege on.</summary>
    private const uint SePrivilegeEnabled = 0x00000002;

    /// <summary>Access right needed to change which privileges of a token are enabled.</summary>
    private const uint TokenAdjustPrivileges = 0x0020;

    /// <summary>Access right needed to read a token's contents.</summary>
    private const uint TokenQuery = 0x0008;

    /// <summary>
    /// <c>EFI_OS_INDICATIONS_BOOT_TO_FW_UI</c>: the bit that asks the firmware to show its setup screen
    /// on the next boot. Set it in <c>OsIndications</c> to make the request; the firmware clears it again.
    /// </summary>
    internal const ulong BootToFirmwareUi = 0x0000000000000001;

    /// <summary>Reads a firmware variable into a caller supplied buffer. Returns the number of bytes read, or 0 on failure.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFirmwareEnvironmentVariableW(string name, string guid, byte[] buffer, uint size);

    /// <summary>Writes a firmware variable. Passing a size of 0 would delete it, which this class never does.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFirmwareEnvironmentVariableW(string name, string guid, byte[] buffer, uint size);

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
    /// <c>LayoutKind.Sequential</c> guarantees the fields are laid out in the declared order, which is
    /// what the native side expects.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
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
    /// Switches on the privilege that firmware variable access requires.
    /// </summary>
    /// <remarks>
    /// Must be called before any read or write in this class. The privilege only affects the current
    /// process and disappears when it exits, so nothing needs to be undone.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The privilege could not be enabled, almost always because the process is not elevated.
    /// </exception>
    internal static void EnableFirmwarePrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            throw new InvalidOperationException(
                $"Unable to open the process token to enable firmware access (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            // A null system name means: look the privilege up on this machine.
            if (!LookupPrivilegeValueW(null, SeSystemEnvironmentName, out var luid))
            {
                throw new InvalidOperationException(
                    $"Unable to look up the {SeSystemEnvironmentName} privilege (Win32 error {Marshal.GetLastWin32Error()}).");
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
                    $"Unable to enable the {SeSystemEnvironmentName} privilege (Win32 error {Marshal.GetLastWin32Error()}). Administrator rights are required.");
            }

            Log.Verbose("Enabled {Privilege} on the current process token", SeSystemEnvironmentName);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>
    /// Reads a firmware variable that holds a 64 bit value, such as <c>OsIndications</c>.
    /// </summary>
    /// <param name="name">Variable name, case sensitive as defined by the UEFI specification.</param>
    /// <returns>
    /// The value, or <see langword="null"/> if the variable does not exist or could not be read - for
    /// example on a machine booted in legacy BIOS mode. A missing variable is a normal condition that
    /// the caller interprets, so this is not treated as an error.
    /// </returns>
    internal static ulong? ReadUInt64(string name)
    {
        var buffer = new byte[8];
        var read = GetFirmwareEnvironmentVariableW(name, EfiGlobalVariableGuid, buffer, (uint)buffer.Length);
        if (read == 0)
        {
            Log.Verbose("Firmware variable {Name} could not be read (Win32 error {Error})", name, Marshal.GetLastWin32Error());
            return null;
        }

        var value = BitConverter.ToUInt64(buffer);
        Log.Verbose("Firmware variable {Name} = 0x{Value:X16}", name, value);
        return value;
    }

    /// <summary>
    /// Writes a 64 bit value to a firmware variable.
    /// </summary>
    /// <remarks>
    /// This overwrites the variable completely. Callers that only want to set individual bits must read
    /// the current value first and combine it themselves.
    /// </remarks>
    /// <param name="name">Variable name, case sensitive as defined by the UEFI specification.</param>
    /// <param name="value">The complete new value.</param>
    /// <exception cref="InvalidOperationException">
    /// The firmware refused the write, typically because the privilege is missing or the variable is
    /// read-only.
    /// </exception>
    internal static void WriteUInt64(string name, ulong value)
    {
        if (!SetFirmwareEnvironmentVariableW(name, EfiGlobalVariableGuid, BitConverter.GetBytes(value), sizeof(ulong)))
        {
            throw new InvalidOperationException(
                $"Unable to write firmware variable {name} (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        Log.Verbose("Firmware variable {Name} set to 0x{Value:X16}", name, value);
    }
}
