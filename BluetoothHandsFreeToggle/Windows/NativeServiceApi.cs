using System.Runtime.InteropServices;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

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

    // ===== Status, control and enumeration =====

    private const int ScManagerEnumerateService = 0x0004;
    private const int ServiceQueryStatus = 0x0004;
    private const int ServiceStart = 0x0010;
    private const int ServiceStop = 0x0020;
    private const int ScStatusProcessInfo = 0;
    private const int ScEnumProcessInfo = 0;
    private const int ServiceControlStop = 0x00000001;
    private const int ServiceWin32 = 0x00000030;
    private const int ServiceActive = 0x00000001;
    private const int ErrorMoreData = 234;

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        out ServiceStatusProcess buffer,
        int bufferSize,
        out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(
        IntPtr service,
        int control,
        out ServiceStatusData serviceStatus);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(
        IntPtr service,
        int argumentCount,
        IntPtr arguments);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(
        IntPtr serviceControlManager,
        int infoLevel,
        int serviceType,
        int serviceState,
        IntPtr services,
        int bufferSize,
        out int bytesNeeded,
        out int servicesReturned,
        ref int resumeHandle,
        string? groupName);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
        public int ProcessId;
        public int ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusData
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public ServiceStatusProcess Status;
    }

    public static bool TryQueryStatus(
        string serviceName,
        out ServiceStatus? status,
        out int nativeErrorCode,
        out string? error)
    {
        ServiceStatus? result = null;
        var success = WithService(
            serviceName,
            ServiceQueryStatus,
            "QueryServiceStatusEx",
            service =>
            {
                if (!QueryServiceStatusEx(
                        service,
                        ScStatusProcessInfo,
                        out var native,
                        Marshal.SizeOf<ServiceStatusProcess>(),
                        out _))
                {
                    return Marshal.GetLastWin32Error();
                }

                result = ToStatus(native);
                return 0;
            },
            out nativeErrorCode,
            out error);

        status = result;
        return success;
    }

    /// <summary>Sends SERVICE_CONTROL_STOP. Returns the Win32 error code (0 on success).</summary>
    public static int SendStop(string serviceName, out string? error)
    {
        _ = WithService(
            serviceName,
            ServiceStop,
            "ControlService(STOP)",
            service => ControlService(service, ServiceControlStop, out _)
                ? 0
                : Marshal.GetLastWin32Error(),
            out var errorCode,
            out error);
        return errorCode;
    }

    /// <summary>Calls StartService without arguments. Returns the Win32 error code (0 on success).</summary>
    public static int SendStart(string serviceName, out string? error)
    {
        _ = WithService(
            serviceName,
            ServiceStart,
            "StartService",
            service => StartService(service, 0, IntPtr.Zero)
                ? 0
                : Marshal.GetLastWin32Error(),
            out var errorCode,
            out error);
        return errorCode;
    }

    /// <summary>Lists the names of all active Win32 services hosted by <paramref name="processId"/>.</summary>
    public static bool TryGetServicesInProcess(
        int processId,
        out List<string> serviceNames,
        out string? error)
    {
        serviceNames = [];
        error = null;

        var serviceControlManager = OpenSCManager(
            null,
            null,
            ScManagerConnect | ScManagerEnumerateService);
        if (serviceControlManager == IntPtr.Zero)
        {
            error = FormatWin32Error("OpenSCManager", Marshal.GetLastWin32Error());
            return false;
        }

        var buffer = IntPtr.Zero;
        try
        {
            var resumeHandle = 0;
            var bufferSize = 0;
            var entrySize = Marshal.SizeOf<EnumServiceStatusProcess>();

            // Services can start or stop between calls, so keep resuming until the list is complete.
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var finished = EnumServicesStatusEx(
                    serviceControlManager,
                    ScEnumProcessInfo,
                    ServiceWin32,
                    ServiceActive,
                    buffer,
                    bufferSize,
                    out var bytesNeeded,
                    out var returned,
                    ref resumeHandle,
                    null);
                var lastError = finished ? 0 : Marshal.GetLastWin32Error();

                for (var index = 0; index < returned; index++)
                {
                    var entry = Marshal.PtrToStructure<EnumServiceStatusProcess>(
                        IntPtr.Add(buffer, index * entrySize));
                    if (entry.Status.ProcessId != processId)
                        continue;

                    var name = Marshal.PtrToStringUni(entry.ServiceName);
                    if (!string.IsNullOrEmpty(name))
                        serviceNames.Add(name);
                }

                if (finished)
                    return true;

                if (lastError != ErrorMoreData)
                {
                    error = FormatWin32Error("EnumServicesStatusEx", lastError);
                    return false;
                }

                if (bytesNeeded > bufferSize)
                {
                    if (buffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(buffer);

                    // Leave headroom for services that start between the two calls.
                    bufferSize = bytesNeeded + 4096;
                    buffer = Marshal.AllocHGlobal(bufferSize);
                }
            }

            error = FormatWin32Error("EnumServicesStatusEx", ErrorMoreData);
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);

            _ = CloseServiceHandle(serviceControlManager);
        }
    }

    // ===== Failure (recovery) actions =====

    private const int ServiceConfigFailureActions = 2;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryServiceConfig2W")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig2(
        IntPtr service,
        int infoLevel,
        IntPtr buffer,
        int bufferSize,
        out int bytesNeeded);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceFailureActionsData
    {
        public int ResetPeriod;
        public IntPtr RebootMessage;
        public IntPtr Command;
        public int ActionCount;
        public IntPtr Actions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScAction
    {
        public int Type;
        public int Delay;
    }

    /// <summary>
    /// Reads the actions the SCM performs when the service's process terminates unexpectedly.
    /// Terminating a host process triggers them, so a Reboot or RunCommand action must veto it.
    /// </summary>
    public static bool TryGetFailureActions(
        string serviceName,
        out List<ServiceRecoveryAction> actions,
        out string? error)
    {
        var result = new List<ServiceRecoveryAction>();
        var success = WithService(
            serviceName,
            ServiceQueryConfig,
            "QueryServiceConfig2(FAILURE_ACTIONS)",
            service =>
            {
                _ = QueryServiceConfig2(service, ServiceConfigFailureActions, IntPtr.Zero, 0, out var needed);
                if (needed <= 0)
                    return Marshal.GetLastWin32Error();

                var buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!QueryServiceConfig2(service, ServiceConfigFailureActions, buffer, needed, out _))
                        return Marshal.GetLastWin32Error();

                    var data = Marshal.PtrToStructure<ServiceFailureActionsData>(buffer);
                    var size = Marshal.SizeOf<ScAction>();
                    for (var index = 0; index < data.ActionCount && data.Actions != IntPtr.Zero; index++)
                    {
                        var action = Marshal.PtrToStructure<ScAction>(IntPtr.Add(data.Actions, index * size));
                        result.Add(action.Type switch
                        {
                            0 => ServiceRecoveryAction.None,
                            1 => ServiceRecoveryAction.Restart,
                            2 => ServiceRecoveryAction.Reboot,
                            3 => ServiceRecoveryAction.RunCommand,
                            _ => ServiceRecoveryAction.Unknown
                        });
                    }

                    return 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            },
            out _,
            out error);

        actions = result;
        return success;
    }

    private static ServiceStatus ToStatus(ServiceStatusProcess native)
        => new(
            MapRunState(native.CurrentState),
            native.ProcessId,
            native.CheckPoint,
            TimeSpan.FromMilliseconds(Math.Max(0, native.WaitHint)),
            native.Win32ExitCode,
            native.ServiceSpecificExitCode);

    private static ServiceRunState MapRunState(int state)
        => state switch
        {
            1 => ServiceRunState.Stopped,
            2 => ServiceRunState.StartPending,
            3 => ServiceRunState.StopPending,
            4 => ServiceRunState.Running,
            5 => ServiceRunState.ContinuePending,
            6 => ServiceRunState.PausePending,
            7 => ServiceRunState.Paused,
            _ => ServiceRunState.Unknown
        };

    /// <summary>
    /// Opens the service with <paramref name="serviceAccess"/> and runs <paramref name="action"/>,
    /// which returns a Win32 error code captured right after its P/Invoke (0 on success).
    /// </summary>
    private static bool WithService(
        string serviceName,
        int serviceAccess,
        string operation,
        Func<IntPtr, int> action,
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
            var service = OpenService(serviceControlManager, serviceName, serviceAccess);
            if (service == IntPtr.Zero)
            {
                nativeErrorCode = Marshal.GetLastWin32Error();
                error = FormatWin32Error("OpenService", nativeErrorCode);
                return false;
            }

            try
            {
                nativeErrorCode = action(service);
                if (nativeErrorCode == 0)
                    return true;

                error = FormatWin32Error(operation, nativeErrorCode);
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

    internal static string FormatWin32Error(string operation, int errorCode)
        => Text.Format(
            "service.nativeOperationFailed",
            operation,
            errorCode,
            new System.ComponentModel.Win32Exception(errorCode).Message);
}

internal enum ServiceRecoveryAction
{
    None,
    Restart,
    Reboot,
    RunCommand,
    Unknown
}
