using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StereoKeeper.Windows;

/// <summary>Process queries shared by hung-service recovery and microphone diagnostics.</summary>
internal static class NativeProcessApi
{
    public const int ProcessTerminate = 0x0001;
    public const int ProcessQueryLimitedInformation = 0x1000;
    public const int Synchronize = 0x00100000;

    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int Th32CsSnapProcess = 0x00000002;

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

    /// <returns>The package family name of a packaged (Store/MSIX) app; <c>null</c> for desktop apps.</returns>
    public static string? TryGetPackageFamilyName(SafeProcessHandle process)
    {
        var length = 0;
        var result = GetPackageFamilyName(process, ref length, null);
        if (result != ErrorInsufficientBuffer || length <= 0)
            return null;

        var buffer = new char[length];
        result = GetPackageFamilyName(process, ref length, buffer);
        return result == ErrorSuccess && length > 1
            ? new string(buffer, 0, length - 1)
            : null;
    }

    public static bool TryGetCreationTime(SafeProcessHandle process, out long creationTime)
        => GetProcessTimes(process, out creationTime, out _, out _, out _);

    /// <summary>Parent process IDs of every running process, from one Toolhelp snapshot.</summary>
    public static Dictionary<int, int> GetParentProcessIds()
    {
        var parents = new Dictionary<int, int>();
        using var snapshot = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        if (snapshot.IsInvalid)
            return parents;

        var entry = new ProcessEntry32 { Size = Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32First(snapshot, ref entry))
            return parents;

        do
        {
            parents[entry.ProcessId] = entry.ParentProcessId;
        } while (Process32Next(snapshot, ref entry));

        return parents;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public int Size;
        public int Usage;
        public int ProcessId;
        public IntPtr DefaultHeapId;
        public int ModuleId;
        public int Threads;
        public int ParentProcessId;
        public int PriorityClassBase;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetPackageFamilyName(
        SafeProcessHandle process,
        ref int packageFamilyNameLength,
        [Out] char[]? packageFamilyName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out long creationTime,
        out long exitTime,
        out long kernelTime,
        out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(int flags, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry32 entry);
}
