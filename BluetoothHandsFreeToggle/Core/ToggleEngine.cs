using BluetoothHandsFreeToggle.Windows;

namespace BluetoothHandsFreeToggle.Core;

public sealed class ToggleEngine
{
    private readonly WindowsServiceManager _svc = new();
    private readonly RegistryHelper _reg = new();
    private readonly BackupStore _backup = new();

    public ToggleReport GetStatusReport()
    {
        var snaps = Targets.Services.Select(GetSnapshotSafe).ToList();
        return ToggleReport.Ok("Status", snaps);
    }

    public ToggleReport DisableHandsFree(ToggleOptions options)
    {
        var snapsBefore = Targets.Services.Select(GetSnapshotSafe).ToList();
        _backup.Save(snapsBefore);

        var lines = new List<string>();
        var snapsAfter = new List<ServiceSnapshot>();

        lines.Add("Disabling Hands-Free (HFP) services...");
        lines.Add(options.ApplyRegistry
            ? "Registry hard lock: ENABLED"
            : "Registry hard lock: DISABLED (services only)");

        foreach (var t in Targets.Services)
        {
            var before = GetSnapshotSafe(t);
            if (!before.Exists)
            {
                lines.Add($"- {t.ServiceName}: not present");
                snapsAfter.Add(before);
                continue;
            }

            // 1) Stop if running (best effort)
            var stopped = _svc.TryStopService(t.ServiceName, TimeSpan.FromSeconds(20), out var stopErr);
            if (!stopped && stopErr is not null)
                lines.Add($"- {t.ServiceName}: stop -> {stopErr}");

            // 2) Set startup type to Disabled
            var stOk = _svc.TrySetStartType(t.ServiceName, ServiceStartType.Disabled, out var stErr);
            if (!stOk && stErr is not null)
                lines.Add($"- {t.ServiceName}: set start type -> {stErr}");

            // 3) Registry hard lock (optional)
            if (options.ApplyRegistry)
            {
                var rOk = _reg.TrySetRegistryStartValue(t.ServiceName, 4, out var rErr); // 4 = Disabled
                if (!rOk && rErr is not null)
                    lines.Add($"- {t.ServiceName}: registry lock -> {rErr}");
            }

            snapsAfter.Add(GetSnapshotSafe(t));
        }

        lines.Add("");
        lines.Add("Done.");
        lines.Add("Tip: If an app still keeps old audio mode, close it and reconnect the headset. A reboot can help on stubborn systems.");

        return ToggleReport.Ok("Disable complete", snapsAfter, lines);
    }

    public ToggleReport EnableHandsFree(ToggleOptions options)
    {
        var snapsBefore = Targets.Services.Select(GetSnapshotSafe).ToList();

        // Load backup, if exists.
        _backup.TryLoad(out var backup);

        var lines = new List<string>();
        var snapsAfter = new List<ServiceSnapshot>();

        lines.Add("Enabling Hands-Free (HFP) services...");
        lines.Add(options.ApplyRegistry
            ? "Registry restore: ENABLED (uses backup if available)"
            : "Registry restore: SKIPPED");

        foreach (var t in Targets.Services)
        {
            var before = GetSnapshotSafe(t);
            if (!before.Exists)
            {
                lines.Add($"- {t.ServiceName}: not present");
                snapsAfter.Add(before);
                continue;
            }

            // Determine desired start type & registry Start.
            // Safe default: Manual + Start=3
            var desiredStartType = ServiceStartType.Manual;
            var desiredRegStart = 3;

            if (backup?.Services.TryGetValue(t.ServiceName, out var old) == true)
            {
                // Restore only to safe values; if unknown, fall back to Manual.
                desiredStartType = old.StartType switch
                {
                    ServiceStartType.Auto => ServiceStartType.Auto,
                    ServiceStartType.Manual => ServiceStartType.Manual,
                    _ => ServiceStartType.Manual
                };

                desiredRegStart = old.RegistryStartValue switch
                {
                    2 => 2, // Auto
                    3 => 3, // Manual
                    _ => 3
                };
            }

            // 1) Registry restore (optional)
            if (options.ApplyRegistry)
            {
                var rOk = _reg.TrySetRegistryStartValue(t.ServiceName, desiredRegStart, out var rErr);
                if (!rOk && rErr is not null)
                    lines.Add($"- {t.ServiceName}: registry restore -> {rErr}");
            }

            // 2) Set startup type
            var stOk = _svc.TrySetStartType(t.ServiceName, desiredStartType, out var stErr);
            if (!stOk && stErr is not null)
                lines.Add($"- {t.ServiceName}: set start type -> {stErr}");

            // 3) Start (optional)
            if (options.StartServicesOnEnable)
            {
                var started = _svc.TryStartService(t.ServiceName, TimeSpan.FromSeconds(20), out var startErr);
                if (!started && startErr is not null)
                    lines.Add($"- {t.ServiceName}: start -> {startErr}");
            }

            snapsAfter.Add(GetSnapshotSafe(t));
        }

        lines.Add("");
        lines.Add("Done.");
        lines.Add("Tip: Apps may need restart to detect the restored voice profile.");

        // Save new backup snapshot as current baseline too (best effort)
        _backup.Save(snapsAfter);

        return ToggleReport.Ok("Enable complete", snapsAfter, lines);
    }

    private ServiceSnapshot GetSnapshotSafe(TargetService t)
    {
        try
        {
            return _svc.GetSnapshot(t.ServiceName, t.FriendlyName, _reg);
        }
        catch (Exception ex)
        {
            return new ServiceSnapshot(
                t.ServiceName,
                t.FriendlyName,
                Exists: false,
                RunState: ServiceRunState.Unknown,
                StartType: ServiceStartType.Unknown,
                RegistryStartValue: null,
                Note: ex.Message);
        }
    }
}

public sealed record ToggleReport(string Title, bool Success, List<ServiceSnapshot> Snapshots, List<string> Lines)
{
    public static ToggleReport Ok(string title, List<ServiceSnapshot> snapshots, List<string>? lines = null)
        => new(title, true, snapshots, lines ?? new List<string>());

    public static ToggleReport Fail(string title, IEnumerable<string> lines)
        => new(title, false, new List<ServiceSnapshot>(), lines.ToList());

    public string[] ToPrettyLines()
    {
        var output = new List<string>();
        output.AddRange(Lines);

        if (Snapshots.Count > 0)
        {
            output.Add("");
            output.Add("Services:");
            foreach (var s in Snapshots)
            {
                var exists = s.Exists ? "Yes" : "No";
                var reg = s.RegistryStartValue.HasValue ? s.RegistryStartValue.Value.ToString() : "-";
                var note = string.IsNullOrWhiteSpace(s.Note) ? "" : $" ({s.Note})";
                output.Add($"- {s.ServiceName} | Exists={exists} | State={s.RunState} | Startup={s.StartType} | RegStart={reg}{note}");
            }
        }

        return output.ToArray();
    }

    public string ToPrettyText()
        => string.Join(Environment.NewLine, new[] { Title }.Concat(ToPrettyLines()));
}
