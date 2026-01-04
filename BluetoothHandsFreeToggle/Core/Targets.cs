namespace BluetoothHandsFreeToggle.Core;

public static class Targets
{
    // Keep it conservative & safe: only known HFP-related services.
    public static readonly TargetService[] Services =
    [
        new("BthHFSrv", "Bluetooth Hands-Free Service (HFP)"),
        new("BTAGService", "Bluetooth Audio Gateway (HFP)")
    ];
}

public sealed record TargetService(string ServiceName, string FriendlyName);