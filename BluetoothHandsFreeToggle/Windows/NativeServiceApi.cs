using System.Runtime.InteropServices;

namespace BluetoothHandsFreeToggle.Windows;

internal static class NativeServiceApi
{
    private const int ScManagerConnect = 0x0001;
    private const int ServiceQueryConfig = 0x0001;
    private const int ServiceChangeConfig = 0x0002;
    private const int ServiceNoChange = -1;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenSCManager(
        string? machineName,
        string? databaseName,
        int desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenService(
        IntPtr serviceControlManager,
        string serviceName,
        int desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(
        IntPtr service,
        IntPtr queryServiceConfig,
        int bufferSize,
        out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(
        IntPtr service,
        int serviceType,
        int startType,
        int errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryServiceConfigData
    {
        public int ServiceType;
        public int StartType;
        public int ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public int TagId;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    public static bool TryGetStartType(
        string serviceName,
        out int startType,
        out int nativeErrorCode,
        out string? error)
    {
        startType = -1;
        nativeErrorCode = 0;
        error = null;

        var serviceControlManager = OpenSCManager(null, null, ScManagerConnect);
        if (serviceControlManager == IntPtr.Zero)
        {
            nativeErrorCode = Marshal.GetLastWin32Error();
            error = FormatWin32Error("OpenSCManager", nativeErrorCode);
            return false;
        }

        try
        {
            var service = OpenService(serviceControlManager, serviceName, ServiceQueryConfig);
            if (service == IntPtr.Zero)
            {
                nativeErrorCode = Marshal.GetLastWin32Error();
                error = FormatWin32Error("OpenService", nativeErrorCode);
                return false;
            }

            try
            {
                _ = QueryServiceConfig(service, IntPtr.Zero, 0, out var bytesNeeded);
                if (bytesNeeded <= 0)
                {
                    nativeErrorCode = Marshal.GetLastWin32Error();
                    error = FormatWin32Error("QueryServiceConfig(size)", nativeErrorCode);
                    return false;
                }

                var buffer = Marshal.AllocHGlobal(bytesNeeded);
                try
                {
                    if (!QueryServiceConfig(service, buffer, bytesNeeded, out _))
                    {
                        nativeErrorCode = Marshal.GetLastWin32Error();
                        error = FormatWin32Error("QueryServiceConfig", nativeErrorCode);
                        return false;
                    }

                    startType = Marshal.PtrToStructure<QueryServiceConfigData>(buffer).StartType;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                _ = CloseServiceHandle(service);
            }
        }
        finally
        {
            _ = CloseServiceHandle(serviceControlManager);
        }
    }

    public static bool TrySetStartType(
        string serviceName,
        int startType,
        out int nativeErrorCode,
        out string? error)
    {
        nativeErrorCode = 0;
        error = null;

        var serviceControlManager = OpenSCManager(null, null, ScManagerConnect);
        if (serviceControlManager == IntPtr.Zero)
        {
            nativeErrorCode = Marshal.GetLastWin32Error();
            error = FormatWin32Error("OpenSCManager", nativeErrorCode);
            return false;
        }

        try
        {
            var service = OpenService(serviceControlManager, serviceName, ServiceChangeConfig);
            if (service == IntPtr.Zero)
            {
                nativeErrorCode = Marshal.GetLastWin32Error();
                error = FormatWin32Error("OpenService", nativeErrorCode);
                return false;
            }

            try
            {
                if (ChangeServiceConfig(
                        service,
                        ServiceNoChange,
                        startType,
                        ServiceNoChange,
                        null,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        null,
                        null))
                {
                    return true;
                }

                nativeErrorCode = Marshal.GetLastWin32Error();
                error = FormatWin32Error("ChangeServiceConfig", nativeErrorCode);
                return false;
            }
            finally
            {
                _ = CloseServiceHandle(service);
            }
        }
        finally
        {
            _ = CloseServiceHandle(serviceControlManager);
        }
    }

    private static string FormatWin32Error(string operation, int errorCode)
        => $"{operation} failed with Win32 error {errorCode}: " +
           new System.ComponentModel.Win32Exception(errorCode).Message;
}
