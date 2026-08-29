using System.Runtime.InteropServices;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Reads and writes UEFI global firmware variables on Windows.
/// Requires the SE_SYSTEM_ENVIRONMENT_NAME privilege, which is available to elevated processes but
/// must be explicitly enabled on the process token before the firmware APIs will succeed.
/// </summary>
internal static class WindowsFirmwareVariables
{
    private const string EfiGlobalVariableGuid = "{8be4df61-93ca-11d2-aa0d-00e098032b8c}";
    private const string SeSystemEnvironmentName = "SeSystemEnvironmentPrivilege";
    private const uint SePrivilegeEnabled = 0x00000002;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;

    /// <summary>EFI_OS_INDICATIONS_BOOT_TO_FW_UI - requests the firmware setup UI on the next boot.</summary>
    internal const ulong BootToFirmwareUi = 0x0000000000000001;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFirmwareEnvironmentVariableW(string name, string guid, byte[] buffer, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFirmwareEnvironmentVariableW(string name, string guid, byte[] buffer, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    internal static void EnableFirmwarePrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            throw new InvalidOperationException(
                $"Unable to open the process token to enable firmware access (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        try
        {
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

            // AdjustTokenPrivileges reports success even when the privilege was not actually granted.
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
