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
        return ToggleReport.Ok("STATUS", snaps);
    }

    public ToggleReport DisableHandsFree(ToggleOptions options)
    {
        var before = Targets.Services.Select(GetSnapshotSafe).ToList();
        _backup.Save(before);

        var lines = new List<string>
        {
            "Disabling Hands-Free (HFP) components...",
            options.ApplyRegistry
                ? "Registry hard lock: ENABLED"
                : "Registry hard lock: DISABLED (services only)"
        };

        var after = new List<ServiceSnapshot>();

        foreach (var t in Targets.Services)
        {
            var snapBefore = GetSnapshotSafe(t);

            if (!snapBefore.Exists)
            {
                lines.Add($"- {t.FriendlyName}: Not available on this system");
                after.Add(snapBefore);
                continue;
            }

            // Stop (best effort)
            var stopped = _svc.TryStopService(t.ServiceName, TimeSpan.FromSeconds(20), out var stopErr);
            if (!stopped && stopErr is not null)
                lines.Add($"- {t.FriendlyName}: stop -> {stopErr}");

            // Disable startup type
            var stOk = _svc.TrySetStartType(t.ServiceName, ServiceStartType.Disabled, out var stErr);
            if (!stOk && stErr is not null)
                lines.Add($"- {t.FriendlyName}: startup -> {stErr}");

            // Registry lock (optional)
            if (options.ApplyRegistry)
            {
                var rOk = _reg.TrySetRegistryStartValue(t.ServiceName, 4, out var rErr); // 4 = Disabled
                if (!rOk && rErr is not null)
                    lines.Add($"- {t.FriendlyName}: registry -> {rErr}");
            }

            after.Add(GetSnapshotSafe(t));
        }

        lines.Add("Done.");

        return ToggleReport.Ok("SUCCESS - Disable complete", after, lines);
    }

    public ToggleReport EnableHandsFree(ToggleOptions options)
    {
        var before = Targets.Services.Select(GetSnapshotSafe).ToList();
        _backup.TryLoad(out var backup);

        var lines = new List<string>
        {
            "Enabling Hands-Free (HFP) components...",
            options.ApplyRegistry
                ? "Registry restore: ENABLED (uses backup if available)"
                : "Registry restore: SKIPPED"
        };

        var after = new List<ServiceSnapshot>();

        foreach (var t in Targets.Services)
        {
            var snapBefore = GetSnapshotSafe(t);

            if (!snapBefore.Exists)
            {
                lines.Add($"- {t.FriendlyName}: Not available on this system");
                after.Add(snapBefore);
                continue;
            }

            // Safe defaults: Manual + Start=3
            var desiredStartType = ServiceStartType.Manual;
            var desiredRegStart = 3;

            if (backup?.Services.TryGetValue(t.ServiceName, out var old) == true)
            {
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

            if (options.ApplyRegistry)
            {
                var rOk = _reg.TrySetRegistryStartValue(t.ServiceName, desiredRegStart, out var rErr);
                if (!rOk && rErr is not null)
                    lines.Add($"- {t.FriendlyName}: registry -> {rErr}");
            }

            var stOk = _svc.TrySetStartType(t.ServiceName, desiredStartType, out var stErr);
            if (!stOk && stErr is not null)
                lines.Add($"- {t.FriendlyName}: startup -> {stErr}");

            if (options.StartServicesOnEnable)
            {
                var started = _svc.TryStartService(t.ServiceName, TimeSpan.FromSeconds(20), out var startErr);
                if (!started && startErr is not null)
                    lines.Add($"- {t.FriendlyName}: start -> {startErr}");
            }

            after.Add(GetSnapshotSafe(t));
        }

        lines.Add("Done.");

        _backup.Save(after);

        return ToggleReport.Ok("SUCCESS - Enable complete", after, lines);
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
            output.Add("Components:");
            foreach (var s in Snapshots)
            {
                var exists = s.Exists ? "Yes" : "No";
                var reg = s.RegistryStartValue.HasValue ? s.RegistryStartValue.Value.ToString() : "-";
                var note = string.IsNullOrWhiteSpace(s.Note) ? "" : $" ({s.Note})";

                // Keep technical ID only as a hint, not the main focus
                var tech = s.Exists ? $" [TechId: {s.ServiceName}]" : "";
                output.Add($"- {s.FriendlyName}: Exists={exists} | State={s.RunState} | Startup={s.StartType} | RegStart={reg}{tech}{note}");
            }
        }

        return output.ToArray();
    }

    public string ToPrettyText()
        => string.Join(Environment.NewLine, new[] { Title }.Concat(ToPrettyLines()));
}
