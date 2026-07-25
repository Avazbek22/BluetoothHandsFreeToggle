namespace BluetoothHandsFreeToggle.Core;

public static class Targets
{
    // Keep the list conservative: these are the known Windows Classic HFP services.
    public static IReadOnlyList<TargetService> Services { get; } =
    [
        new("BthHFSrv", "Bluetooth Hands-Free Service (HFP)"),
        new("BTAGService", "Bluetooth Audio Gateway (HFP)")
    ];
}

public sealed record TargetService(string ServiceName, string FriendlyName);
