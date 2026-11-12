using System.Collections.ObjectModel;
using System.Globalization;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Gui.ViewModels;

public enum StepState
{
    Pending,
    Active,
    Done
}

public enum StepKind
{
    Prepare,
    Stop,
    Start,
    Disable,
    Startup,
    Verify
}

public sealed class StepItem(StepKind kind, string? service, string title, string detail) : ObservableObject
{
    private StepState _state;
    private string _detail = detail;

    public StepKind Kind { get; } = kind;
    public string? Service { get; } = service;
    public string Title { get; } = title;

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public StepState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsPending));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsDone));
            }
        }
    }

    public bool IsPending => State is StepState.Pending;
    public bool IsActive => State is StepState.Active;
    public bool IsDone => State is StepState.Done;
}

/// <summary>
/// Turns the engine's progress events into a short checklist: the plan is predicted from the
/// current service state, and events move through it (unexpected steps are inserted as they come).
/// </summary>
public sealed class OperationSteps
{
    private readonly bool _multipleServices;
    private TimeSpan _activeSince;
    private double _reported;

    public OperationSteps(OperationKind operation, IReadOnlyList<ServiceSnapshot> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Operation = operation;
        var present = services.Where(service => service.Exists).ToList();

        // A soft reset only restarts services that are running; the others are left alone.
        var involved = operation == OperationKind.Soft
            ? present.Where(service =>
                service.StartType is not ServiceStartType.Disabled &&
                service.RunState is not ServiceRunState.Stopped).ToList()
            : present;

        // Step titles name the service only when there is more than one to tell apart.
        _multipleServices = involved.Count > 1;

        switch (operation)
        {
            case OperationKind.Soft:
                foreach (var service in involved)
                {
                    Add(StepKind.Stop, service.FriendlyName);
                    Add(StepKind.Start, service.FriendlyName);
                }

                break;

            case OperationKind.Hard:
                Add(StepKind.Prepare, null);
                foreach (var service in involved)
                {
                    Add(StepKind.Disable, service.FriendlyName);
                    Add(StepKind.Stop, service.FriendlyName);
                }

                break;

            default:
                Add(StepKind.Prepare, null);
                foreach (var service in involved)
                {
                    Add(StepKind.Startup, service.FriendlyName);
                    Add(StepKind.Start, service.FriendlyName);
                }

                break;
        }

        Add(StepKind.Verify, null);
    }

    public OperationKind Operation { get; }

    public ObservableCollection<StepItem> Items { get; } = [];

    public StepItem? Active => Items.FirstOrDefault(item => item.IsActive);

    /// <summary>Marks the first step active once the operation actually runs (after UAC).</summary>
    public void Begin(TimeSpan now)
    {
        if (Active is null && Items.Count > 0 && Items.All(item => item.IsPending))
            Activate(Items[0], now);
    }

    public void Apply(OperationEvent operationEvent, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(operationEvent);

        var kind = operationEvent.Step switch
        {
            OperationStep.Stopping or OperationStep.WaitingStop => StepKind.Stop,
            OperationStep.Starting or OperationStep.WaitingStart => StepKind.Start,
            OperationStep.Disabling => StepKind.Disable,
            OperationStep.SettingStartup => StepKind.Startup,
            _ => (StepKind?)null
        };

        var target = Active;
        if (kind is { } stepKind &&
            (target is null || target.Kind != stepKind || !SameService(target.Service, operationEvent.Service)))
        {
            target = FindNext(stepKind, operationEvent.Service) ?? Insert(stepKind, operationEvent.Service);
            Activate(target, now);
        }

        if (target is null)
            return;

        var seconds = WholeSeconds(operationEvent.Elapsed);
        target.Detail = operationEvent.Step switch
        {
            OperationStep.WaitingStop => Text.Format("gui.step.detail.waitStop", seconds),
            OperationStep.WaitingStart => Text.Format("gui.step.detail.waitStart", seconds),
            OperationStep.WaitingSettle => Text.Format("gui.step.detail.waitSettle", seconds),
            OperationStep.NotResponding => Text.Get("gui.step.detail.notResponding"),
            OperationStep.Terminated => Text.Get("gui.step.detail.terminated"),
            _ => target.Detail
        };
    }

    /// <summary>The operation returned: everything ran, the result is being checked.</summary>
    public void Complete(TimeSpan now)
    {
        var verify = Items.LastOrDefault(item => item.Kind is StepKind.Verify);
        if (verify is not null)
            Activate(verify, now);

        foreach (var item in Items)
            item.State = StepState.Done;
    }

    /// <summary>
    /// A ring value for the operation: finished steps plus an easing share of the active one, so the
    /// ring keeps moving during long waits without ever claiming to be finished.
    /// </summary>
    public double EstimateProgress(TimeSpan now)
    {
        if (Items.Count == 0)
            return 0;

        var done = Items.Count(item => item.IsDone);
        var share = 0.0;
        if (Active is not null)
        {
            var seconds = Math.Max(0, (now - _activeSince).TotalSeconds);
            share = 0.85 * (1 - Math.Exp(-seconds / 2.5));
        }

        var value = Math.Min(1, (done + share) / Items.Count);
        _reported = Math.Max(_reported, value);
        return _reported;
    }

    private void Activate(StepItem item, TimeSpan now)
    {
        var index = Items.IndexOf(item);
        for (var i = 0; i < index; i++)
            Items[i].State = StepState.Done;

        if (!item.IsActive)
        {
            item.State = StepState.Active;
            _activeSince = now;
        }
    }

    private StepItem? FindNext(StepKind kind, string? service)
    {
        var start = Active is { } active ? Items.IndexOf(active) + 1 : 0;
        for (var i = start; i < Items.Count; i++)
        {
            if (Items[i].Kind == kind && Items[i].IsPending && SameService(Items[i].Service, service))
                return Items[i];
        }

        return null;
    }

    private StepItem Insert(StepKind kind, string? service)
    {
        var item = Create(kind, service);
        var index = Active is { } active ? Items.IndexOf(active) + 1 : 0;
        var verify = Items.LastOrDefault(existing => existing.Kind is StepKind.Verify);
        if (verify is not null)
            index = Math.Min(index, Items.IndexOf(verify));

        Items.Insert(index, item);
        return item;
    }

    private void Add(StepKind kind, string? service) => Items.Add(Create(kind, service));

    private StepItem Create(StepKind kind, string? service)
    {
        var shortName = service is null ? null : ShortServiceName(service);
        var (titleKey, namedTitleKey, detailKey) = kind switch
        {
            StepKind.Prepare when Operation is OperationKind.Hard => ("gui.step.save", null, "gui.step.detail.save"),
            StepKind.Prepare => ("gui.step.read", null, "gui.step.detail.read"),
            StepKind.Stop => ("gui.step.stop", "gui.step.stopNamed", "gui.step.detail.stopRequested"),
            StepKind.Start => ("gui.step.start", "gui.step.startNamed", "gui.step.detail.startRequested"),
            StepKind.Disable => ("gui.step.disable", "gui.step.disableNamed", "gui.step.detail.disable"),
            StepKind.Startup => ("gui.step.startup", "gui.step.startupNamed", "gui.step.detail.startup"),
            _ => ("gui.step.verify", (string?)null, "gui.step.detail.verify")
        };

        var title = _multipleServices && namedTitleKey is not null && shortName is not null
            ? Text.Format(namedTitleKey, shortName)
            : Text.Get(titleKey);

        return new StepItem(kind, service, title, Text.Get(detailKey));
    }

    private static bool SameService(string? left, string? right)
        => left is null || right is null || string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>"Bluetooth Audio Gateway (HFP)" becomes "Bluetooth Audio Gateway".</summary>
    public static string ShortServiceName(string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(friendlyName);
        var bracket = friendlyName.LastIndexOf(" (", StringComparison.Ordinal);
        return bracket > 0 ? friendlyName[..bracket] : friendlyName;
    }

    private static string WholeSeconds(TimeSpan? elapsed)
        => Math.Max(0, (int)(elapsed ?? TimeSpan.Zero).TotalSeconds).ToString(CultureInfo.CurrentCulture);
}
