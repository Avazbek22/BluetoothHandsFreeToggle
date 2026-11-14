using System.Runtime.InteropServices;
using BluetoothHandsFreeToggle.Core;
using Microsoft.Win32.SafeHandles;

namespace BluetoothHandsFreeToggle.Windows;

/// <summary>
/// Last-resort recovery for a service that hangs in a pending state. A hung service cannot be
/// stopped or started through the SCM; the only alternatives are terminating its host process or
/// restarting Windows. Termination is performed only when it affects nothing but the allowed
/// services, and every condition is re-checked while a handle to the process is held, so the
/// PID cannot be recycled between the check and the termination.
/// </summary>
internal static class ServiceHostTerminator
{
    private const int WaitObject0 = 0;
    private const int WaitTimeoutMilliseconds = 5000;

    // ERROR_PROCESS_ABORTED: what the SCM records as the host exit code.
    private const int TerminationExitCode = 1067;

    public static HostTerminationResult Terminate(
        string serviceName,
        int expectedProcessId,
        IReadOnlyCollection<string> allowedServices)
    {
        if (expectedProcessId <= 4 || expectedProcessId == Environment.ProcessId)
        {
            return HostTerminationResult.UnexpectedHost(
                $"PID {expectedProcessId} is not a service host process.");
        }

        // Cheap, unprivileged checks first: most refusals never need a process handle.
        var precheck = Verify(serviceName, expectedProcessId, allowedServices);
        if (precheck is not null)
            return precheck;

        using var debugPrivilege = DebugPrivilege.TryEnable();

        using var process = NativeProcessApi.Open(
            NativeProcessApi.ProcessTerminate |
            NativeProcessApi.ProcessQueryLimitedInformation |
            NativeProcessApi.Synchronize,
            expectedProcessId);
        if (process.IsInvalid)
        {
            return HostTerminationResult.Failed(
                NativeServiceApi.FormatWin32Error("OpenProcess", Marshal.GetLastWin32Error()));
        }

        // Re-verify while the handle is held: the PID cannot be recycled from here on.
        var verified = Verify(serviceName, expectedProcessId, allowedServices);
        if (verified is not null)
            return verified;

        var imagePath = NativeProcessApi.TryGetImagePath(process, out var imageError);
        if (imagePath is null)
            return HostTerminationResult.Failed(imageError ?? "QueryFullProcessImageName");

        var expectedImage = Path.Combine(Environment.SystemDirectory, "svchost.exe");
        if (!string.Equals(
                Path.GetFullPath(imagePath),
                Path.GetFullPath(expectedImage),
                StringComparison.OrdinalIgnoreCase))
        {
            return HostTerminationResult.UnexpectedHost(imagePath);
        }

        if (!TerminateProcess(process, TerminationExitCode))
        {
            var terminateError = Marshal.GetLastWin32Error();

            // The process may have exited by itself in the meantime.
            if (WaitForSingleObject(process, 0) == WaitObject0)
                return HostTerminationResult.Terminated;

            return HostTerminationResult.Failed(
                NativeServiceApi.FormatWin32Error("TerminateProcess", terminateError));
        }

        return WaitForSingleObject(process, WaitTimeoutMilliseconds) == WaitObject0
            ? HostTerminationResult.Terminated
            : HostTerminationResult.Failed(
                $"PID {expectedProcessId} did not exit within {WaitTimeoutMilliseconds / 1000} s.");
    }

    /// <returns><c>null</c> when terminating the process is allowed; otherwise the refusal.</returns>
    private static HostTerminationResult? Verify(
        string serviceName,
        int expectedProcessId,
        IReadOnlyCollection<string> allowedServices)
    {
        // 1. The service must still be pending in exactly this process.
        if (!NativeServiceApi.TryQueryStatus(serviceName, out var status, out _, out var statusError))
            return HostTerminationResult.Failed(statusError ?? serviceName);

        if (status!.ProcessId != expectedProcessId || !status.IsPending)
            return HostTerminationResult.StateChanged;

        // 2. The process must host nothing but allowed services.
        if (!NativeServiceApi.TryGetServicesInProcess(expectedProcessId, out var hosted, out var enumError))
            return HostTerminationResult.Failed(enumError ?? "EnumServicesStatusEx");

        var others = hosted
            .Where(name => !allowedServices.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (others.Count > 0)
            return HostTerminationResult.SharedHost(others);

        if (!hosted.Contains(serviceName, StringComparer.OrdinalIgnoreCase))
            return HostTerminationResult.StateChanged;

        // 3. Terminating the process triggers the SCM recovery actions of every hosted service.
        //    Restart is expected and handled; Reboot or RunCommand must never be triggered.
        var unsafeServices = new List<string>();
        foreach (var name in hosted.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!NativeServiceApi.TryGetFailureActions(name, out var actions, out var actionsError))
                return HostTerminationResult.Failed(actionsError ?? name);

            if (actions.Any(action => action is
                    ServiceRecoveryAction.Reboot or
                    ServiceRecoveryAction.RunCommand or
                    ServiceRecoveryAction.Unknown))
            {
                unsafeServices.Add(name);
            }
        }

        return unsafeServices.Count > 0
            ? HostTerminationResult.UnsafeRecoveryAction(unsafeServices)
            : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WaitForSingleObject(SafeProcessHandle handle, int milliseconds);

    /// <summary>
    /// Enables SeDebugPrivilege for the duration of the termination. Administrators hold it but it
    /// is disabled by default; without it the service host's DACL may deny PROCESS_TERMINATE.
    /// Failure is not fatal: OpenProcess then reports the real access error.
    /// </summary>
    private sealed class DebugPrivilege : IDisposable
    {
        private const int TokenAdjustPrivileges = 0x0020;
        private const int TokenQuery = 0x0008;
        private const int SePrivilegeEnabled = 0x00000002;
        private const int ErrorNotAllAssigned = 1300;

        private readonly SafeAccessTokenHandle _token;
        private readonly Luid _luid;

        private DebugPrivilege(SafeAccessTokenHandle token, Luid luid)
        {
            _token = token;
            _luid = luid;
        }

        public static DebugPrivilege? TryEnable()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
                return null;

            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid) ||
                !Adjust(token, luid, SePrivilegeEnabled))
            {
                token.Dispose();
                return null;
            }

            return new DebugPrivilege(token, luid);
        }

        public void Dispose()
        {
            _ = Adjust(_token, _luid, 0);
            _token.Dispose();
        }

        private static bool Adjust(SafeAccessTokenHandle token, Luid luid, int attributes)
        {
            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = attributes
            };

            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) &&
                   Marshal.GetLastWin32Error() != ErrorNotAllAssigned;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public int PrivilegeCount;
            public Luid Luid;
            public int Attributes;
        }

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(
            IntPtr processHandle,
            int desiredAccess,
            out SafeAccessTokenHandle tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(
            string? systemName,
            string name,
            out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(
            SafeAccessTokenHandle tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TokenPrivileges newState,
            int bufferLength,
            IntPtr previousState,
            IntPtr returnLength);
    }
}
