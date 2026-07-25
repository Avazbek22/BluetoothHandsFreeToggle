using System.ServiceProcess;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Windows;

public sealed class WindowsServiceManager : IServiceManager
{
    private const int ErrorServiceDoesNotExist = 1060;

    public ServiceSnapshot GetSnapshot(TargetService target)
    {
        var configRead = NativeServiceApi.TryGetStartType(
            target.ServiceName,
            out var nativeStartType,
            out var nativeErrorCode,
            out var configError);

        if (!configRead)
        {
            var notFound = nativeErrorCode == ErrorServiceDoesNotExist;
            return new ServiceSnapshot(
                target.ServiceName,
                target.FriendlyName,
                Exists: false,
                QuerySucceeded: notFound,
                RunState: notFound ? ServiceRunState.NotPresent : ServiceRunState.Unknown,
                StartType: notFound ? ServiceStartType.NotPresent : ServiceStartType.Unknown,
                NativeStartValue: null,
                Note: notFound ? null : configError);
        }

        var runState = GetRunState(target.ServiceName, out var runStateError);
        var startType = MapStartType(nativeStartType);

        return new ServiceSnapshot(
            target.ServiceName,
            target.FriendlyName,
            Exists: true,
            QuerySucceeded: runStateError is null,
            RunState: runState,
            StartType: startType,
            NativeStartValue: nativeStartType,
            Note: runStateError);
    }

    public bool TryStopService(string serviceName, TimeSpan timeout, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            using var controller = new ServiceController(serviceName);
            controller.Refresh();

            if (controller.Status is ServiceControllerStatus.Stopped)
                return true;

            if (controller.Status is ServiceControllerStatus.StopPending)
            {
                controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
                return true;
            }

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            return true;
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            errorMessage = Text.Format(
                "service.timeoutError",
                timeout.TotalSeconds,
                ex.Message);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            errorMessage = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            errorMessage = Text.Format(
                "service.win32Error",
                ex.NativeErrorCode,
                ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryStartService(string serviceName, TimeSpan timeout, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            using var controller = new ServiceController(serviceName);
            controller.Refresh();

            switch (controller.Status)
            {
                case ServiceControllerStatus.Running:
                    return true;

                case ServiceControllerStatus.StartPending:
                case ServiceControllerStatus.ContinuePending:
                    controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
                    return true;

                case ServiceControllerStatus.StopPending:
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
                    controller.Refresh();
                    break;

                case ServiceControllerStatus.Paused:
                    controller.Continue();
                    controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
                    return true;

                case ServiceControllerStatus.PausePending:
                    controller.WaitForStatus(ServiceControllerStatus.Paused, timeout);
                    controller.Continue();
                    controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
                    return true;
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
            return true;
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            errorMessage = Text.Format(
                "service.timeoutError",
                timeout.TotalSeconds,
                ex.Message);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            errorMessage = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            errorMessage = Text.Format(
                "service.win32Error",
                ex.NativeErrorCode,
                ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TrySetStartType(
        string serviceName,
        ServiceStartType startType,
        out string? errorMessage)
    {
        var nativeStartType = startType switch
        {
            ServiceStartType.Auto => 2,
            ServiceStartType.Manual => 3,
            ServiceStartType.Disabled => 4,
            _ => -1
        };

        if (nativeStartType < 0)
        {
            errorMessage = Text.Format(
                "service.unsupportedStartupType",
                ServiceStateText.Get(startType));
            return false;
        }

        return NativeServiceApi.TrySetStartType(serviceName, nativeStartType, out _, out errorMessage);
    }

    private static ServiceRunState GetRunState(string serviceName, out string? error)
    {
        error = null;
        try
        {
            using var controller = new ServiceController(serviceName);
            controller.Refresh();

            return controller.Status switch
            {
                ServiceControllerStatus.Running => ServiceRunState.Running,
                ServiceControllerStatus.Stopped => ServiceRunState.Stopped,
                ServiceControllerStatus.StartPending => ServiceRunState.StartPending,
                ServiceControllerStatus.StopPending => ServiceRunState.StopPending,
                ServiceControllerStatus.Paused => ServiceRunState.Paused,
                ServiceControllerStatus.PausePending => ServiceRunState.PausePending,
                ServiceControllerStatus.ContinuePending => ServiceRunState.ContinuePending,
                _ => ServiceRunState.Unknown
            };
        }
        catch (InvalidOperationException ex)
        {
            error = ex.InnerException?.Message ?? ex.Message;
            return ServiceRunState.Unknown;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            error = Text.Format(
                "service.win32Error",
                ex.NativeErrorCode,
                ex.Message);
            return ServiceRunState.Unknown;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return ServiceRunState.Unknown;
        }
    }

    private static ServiceStartType MapStartType(int nativeStartType)
        => nativeStartType switch
        {
            2 => ServiceStartType.Auto,
            3 => ServiceStartType.Manual,
            4 => ServiceStartType.Disabled,
            _ => ServiceStartType.Unknown
        };
}
