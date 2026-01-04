using System.Runtime.InteropServices;

namespace BluetoothHandsFreeToggle.Windows;

internal static class NativeServiceApi
{
    private const int SC_MANAGER_CONNECT = 0x0001;

    private const int SERVICE_QUERY_CONFIG = 0x0001;
    private const int SERVICE_CHANGE_CONFIG = 0x0002;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, int dwAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, int dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryServiceConfig(
        IntPtr hService,
        IntPtr queryServiceConfigPtr,
        int cbBufSize,
        out int pcbBytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig(
        IntPtr hService,
        int dwServiceType,
        int dwStartType,
        int dwErrorControl,
        string? lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword,
        string? lpDisplayName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct QUERY_SERVICE_CONFIG
    {
        public int dwServiceType;
        public int dwStartType;
        public int dwErrorControl;
        public IntPtr lpBinaryPathName;
        public IntPtr lpLoadOrderGroup;
        public int dwTagId;
        public IntPtr lpDependencies;
        public IntPtr lpServiceStartName;
        public IntPtr lpDisplayName;
    }

    public static bool TryGetStartType(string serviceName, out int startType, out string? error)
    {
        startType = -1;
        error = null;

        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
        {
            error = "OpenSCManager failed: " + Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            var svc = OpenService(scm, serviceName, SERVICE_QUERY_CONFIG);
            if (svc == IntPtr.Zero)
            {
                var code = Marshal.GetLastWin32Error();
                error = code == 1060 ? "Service not found." : "OpenService failed: " + code;
                return false;
            }

            try
            {
                // First call to get required size
                QueryServiceConfig(svc, IntPtr.Zero, 0, out var needed);
                if (needed <= 0)
                {
                    error = "QueryServiceConfig size query failed: " + Marshal.GetLastWin32Error();
                    return false;
                }

                var ptr = Marshal.AllocHGlobal(needed);
                try
                {
                    var ok = QueryServiceConfig(svc, ptr, needed, out _);
                    if (!ok)
                    {
                        error = "QueryServiceConfig failed: " + Marshal.GetLastWin32Error();
                        return false;
                    }

                    var qsc = Marshal.PtrToStructure<QUERY_SERVICE_CONFIG>(ptr);
                    startType = qsc.dwStartType;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static bool TrySetStartType(string serviceName, int startType, out string? error)
    {
        error = null;

        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
        {
            error = "OpenSCManager failed: " + Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            var svc = OpenService(scm, serviceName, SERVICE_CHANGE_CONFIG);
            if (svc == IntPtr.Zero)
            {
                var code = Marshal.GetLastWin32Error();
                error = code == 1060 ? "Service not found." : "OpenService failed: " + code;
                return false;
            }

            try
            {
                // Keep other parameters unchanged by passing SERVICE_NO_CHANGE for those.
                const int SERVICE_NO_CHANGE = -1;

                var ok = ChangeServiceConfig(
                    svc,
                    SERVICE_NO_CHANGE,
                    startType,
                    SERVICE_NO_CHANGE,
                    null,
                    null,
                    IntPtr.Zero,
                    null,
                    null,
                    null,
                    null);

                if (!ok)
                {
                    error = "ChangeServiceConfig failed: " + Marshal.GetLastWin32Error();
                    return false;
                }

                return true;
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }
}
