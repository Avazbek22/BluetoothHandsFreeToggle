using System.ServiceProcess;
using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Windows;

public sealed class WindowsServiceManager
{
    public ServiceSnapshot GetSnapshot(string serviceName, string friendlyName, RegistryHelper registry)
    {
        var exists = ServiceExists(serviceName);

        if (!exists)
        {
            return new ServiceSnapshot(
                serviceName,
                friendlyName,
                Exists: false,
                RunState: ServiceRunState.NotPresent,
                StartType: ServiceStartType.NotPresent,
                RegistryStartValue: registry.TryGetRegistryStartValue(serviceName),
                Note: null);
        }

        var runState = GetRunState(serviceName);
        var startType = GetStartType(serviceName, out var stNote);

        var reg = registry.TryGetRegistryStartValue(serviceName);
        var note = stNote;

        return new ServiceSnapshot(
            serviceName,
            friendlyName,
            Exists: true,
            RunState: runState,
            StartType: startType,
            RegistryStartValue: reg,
            Note: note);
    }

    public bool TryStopService(string serviceName, TimeSpan timeout, out string? error)
    {
        error = null;

        if (!ServiceExists(serviceName))
            return true;

        try
        {
            using var sc = new ServiceController(serviceName);

            if (sc.Status is ServiceControllerStatus.Stopped)
                return true;

            if (sc.Status is ServiceControllerStatus.StopPending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
                return true;
            }

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = "Stop failed: " + ex.Message;
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            error = "Stop failed: " + ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = "Stop failed: " + ex.Message;
            return false;
        }
    }

    public bool TryStartService(string serviceName, TimeSpan timeout, out string? error)
    {
        error = null;

        if (!ServiceExists(serviceName))
            return true;

        try
        {
            using var sc = new ServiceController(serviceName);

            if (sc.Status is ServiceControllerStatus.Running)
                return true;

            if (sc.Status is ServiceControllerStatus.StartPending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
                return true;
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = "Start failed: " + ex.Message;
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            error = "Start failed: " + ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = "Start failed: " + ex.Message;
            return false;
        }
    }

    public bool TrySetStartType(string serviceName, ServiceStartType startType, out string? error)
    {
        error = null;

        if (!ServiceExists(serviceName))
            return true;

        var native = startType switch
        {
            ServiceStartType.Auto => 2,
            ServiceStartType.Manual => 3,
            ServiceStartType.Disabled => 4,
            _ => 3
        };

        var ok = NativeServiceApi.TrySetStartType(serviceName, native, out var err);
        if (!ok)
        {
            error = err;
            return false;
        }

        return true;
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            // Enumerating all services is heavier; this is fast enough and safe.
            using var sc = new ServiceController(serviceName);
            _ = sc.Status; // triggers lookup
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ServiceRunState GetRunState(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);

            return sc.Status switch
            {
                ServiceControllerStatus.Running => ServiceRunState.Running,
                ServiceControllerStatus.Stopped => ServiceRunState.Stopped,
                ServiceControllerStatus.StartPending => ServiceRunState.StartPending,
                ServiceControllerStatus.StopPending => ServiceRunState.StopPending,
                ServiceControllerStatus.Paused => ServiceRunState.Paused,
                _ => ServiceRunState.Unknown
            };
        }
        catch
        {
            return ServiceRunState.Unknown;
        }
    }

    private static ServiceStartType GetStartType(string serviceName, out string? note)
    {
        note = null;

        var ok = NativeServiceApi.TryGetStartType(serviceName, out var st, out var err);
        if (!ok)
        {
            note = err;
            return ServiceStartType.Unknown;
        }

        return st switch
        {
            2 => ServiceStartType.Auto,
            3 => ServiceStartType.Manual,
            4 => ServiceStartType.Disabled,
            _ => ServiceStartType.Unknown
        };
    }
}
