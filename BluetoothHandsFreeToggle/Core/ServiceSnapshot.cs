namespace BluetoothHandsFreeToggle.Core;

public enum ServiceRunState
{
    NotPresent,
    Running,
    Stopped,
    StartPending,
    StopPending,
    Paused,
    PausePending,
    ContinuePending,
    Unknown
}

public enum ServiceStartType
{
    NotPresent,
    Auto,
    Manual,
    Disabled,
    Unknown
}

public sealed record ServiceSnapshot(
    string ServiceName,
    string FriendlyName,
    bool Exists,
    bool QuerySucceeded,
    ServiceRunState RunState,
    ServiceStartType StartType,
    int? NativeStartValue,
    string? Note);
