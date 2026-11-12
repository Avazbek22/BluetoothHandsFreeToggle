using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BluetoothHandsFreeToggle.Windows;

/// <summary>Process queries used by hung-service recovery.</summary>
internal static class NativeProcessApi
{
    public const int ProcessTerminate = 0x0001;
    public const int ProcessQueryLimitedInformation = 0x1000;
    public const int Synchronize = 0x00100000;

    public static SafeProcessHandle Open(int access, int processId)
        => OpenProcess(access, false, processId);

    public static string? TryGetImagePath(SafeProcessHandle process, out string? error)
    {
        error = null;
        var buffer = new char[32768];
        var length = buffer.Length;

        if (!QueryFullProcessImageName(process, 0, buffer, ref length))
        {
            error = NativeServiceApi.FormatWin32Error(
                "QueryFullProcessImageName",
                Marshal.GetLastWin32Error());
            return null;
        }

        return new string(buffer, 0, length);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(
        int desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        int flags,
        [Out] char[] exeName,
        ref int size);
}
