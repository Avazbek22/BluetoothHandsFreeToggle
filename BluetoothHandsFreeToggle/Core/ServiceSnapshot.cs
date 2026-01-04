namespace BluetoothHandsFreeToggle.Core;

public enum ServiceRunState
{
    NotPresent,
    Running,
    Stopped,
    StartPending,
    StopPending,
    Paused,
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
    ServiceRunState RunState,
    ServiceStartType StartType,
    int? RegistryStartValue,
    string? Note);