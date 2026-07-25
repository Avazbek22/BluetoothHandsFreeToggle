using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Localization;

public static class ServiceStateText
{
    public static string Get(ServiceRunState state)
        => Text.Get(state switch
        {
            ServiceRunState.NotPresent => "state.run.notPresent",
            ServiceRunState.Running => "state.run.running",
            ServiceRunState.Stopped => "state.run.stopped",
            ServiceRunState.StartPending => "state.run.startPending",
            ServiceRunState.StopPending => "state.run.stopPending",
            ServiceRunState.Paused => "state.run.paused",
            ServiceRunState.PausePending => "state.run.pausePending",
            ServiceRunState.ContinuePending => "state.run.continuePending",
            _ => "state.run.unknown"
        });

    public static string Get(ServiceStartType startType)
        => Text.Get(startType switch
        {
            ServiceStartType.NotPresent => "state.start.notPresent",
            ServiceStartType.Auto => "state.start.auto",
            ServiceStartType.Manual => "state.start.manual",
            ServiceStartType.Disabled => "state.start.disabled",
            _ => "state.start.unknown"
        });
}
