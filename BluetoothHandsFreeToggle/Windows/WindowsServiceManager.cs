using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Windows;

public sealed class WindowsServiceManager : IServiceManager
{
    public ServiceSnapshot GetSnapshot(TargetService target)
    {
        var configRead = NativeServiceApi.TryGetStartType(
            target.ServiceName,
            out var nativeStartType,
            out var nativeErrorCode,
            out var configError);

        if (!configRead)
        {
            var notFound = nativeErrorCode == Win32ErrorCodes.ServiceDoesNotExist;
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

        var statusRead = NativeServiceApi.TryQueryStatus(
            target.ServiceName,
            out var status,
            out _,
            out var statusError);

        return new ServiceSnapshot(
            target.ServiceName,
            target.FriendlyName,
            Exists: true,
            QuerySucceeded: statusRead,
            RunState: status?.RunState ?? ServiceRunState.Unknown,
            StartType: MapStartType(nativeStartType),
            NativeStartValue: nativeStartType,
            Note: statusRead ? null : statusError);
    }

    public ServiceQueryResult QueryStatus(string serviceName)
        => NativeServiceApi.TryQueryStatus(serviceName, out var status, out _, out var error)
            ? ServiceQueryResult.Ok(status!)
            : ServiceQueryResult.Failed(error ?? Text.Get("service.unknownError"));

    public ServiceCommandResult SendStop(string serviceName)
    {
        var errorCode = NativeServiceApi.SendStop(serviceName, out var error);
        return errorCode switch
        {
            0 => ServiceCommandResult.Accepted,
            Win32ErrorCodes.ServiceNotActive => ServiceCommandResult.AlreadyInState,
            Win32ErrorCodes.ServiceCannotAcceptControl => ServiceCommandResult.NotAccepted,
            _ => ServiceCommandResult.Failed(errorCode, error ?? Text.Get("service.unknownError"))
        };
    }

    public ServiceCommandResult SendStart(string serviceName)
    {
        var errorCode = NativeServiceApi.SendStart(serviceName, out var error);
        return errorCode switch
        {
            0 => ServiceCommandResult.Accepted,
            Win32ErrorCodes.ServiceAlreadyRunning => ServiceCommandResult.AlreadyInState,
            _ => ServiceCommandResult.Failed(errorCode, error ?? Text.Get("service.unknownError"))
        };
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

    public HostTerminationResult TerminateHungServiceHost(
        string serviceName,
        int expectedProcessId,
        IReadOnlyCollection<string> allowedServices)
    {
        try
        {
            return ServiceHostTerminator.Terminate(serviceName, expectedProcessId, allowedServices);
        }
        catch (Exception ex)
        {
            return HostTerminationResult.Failed(ex.Message);
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
