using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Gui.Services;

/// <summary>Read-only live state for the panel and the tray icon. Called on a background thread.</summary>
public interface IStatusSource
{
    IReadOnlyList<ServiceSnapshot> GetServices();
    MicrophoneDiagnosis? DiagnoseMicrophone();
}

public enum OperationOutcomeKind
{
    Completed,

    /// <summary>The UAC prompt was declined; nothing ran.</summary>
    Cancelled,

    /// <summary>The operation could not be started or its result was lost.</summary>
    Failed
}

public sealed record OperationOutcome(OperationOutcomeKind Kind, ToggleReport? Report = null, string? Error = null)
{
    public static OperationOutcome Completed(ToggleReport report) => new(OperationOutcomeKind.Completed, report);
    public static OperationOutcome Cancelled { get; } = new(OperationOutcomeKind.Cancelled);
    public static OperationOutcome Failed(string error) => new(OperationOutcomeKind.Failed, Error: error);
}

/// <summary>Runs a service operation with administrator rights.</summary>
public interface IOperationRunner
{
    /// <param name="onEvent">Progress, called on a background thread in operation order.</param>
    /// <param name="onElevated">Called once the operation is about to run with administrator rights.</param>
    Task<OperationOutcome> RunAsync(
        OperationKind operation,
        Action<OperationEvent> onEvent,
        Action? onElevated = null,
        CancellationToken cancellationToken = default);
}

public interface IAutostart
{
    bool IsEnabled { get; }
    bool TrySetEnabled(bool enabled);
}

public sealed class GuiSettings
{
    public bool NotifyCallMode { get; set; } = true;
    public bool NotifyMicrophone { get; set; } = true;
}

public interface IGuiSettingsStore
{
    GuiSettings Load();
    void Save(GuiSettings settings);
}

/// <summary>Things outside the panel: the browser, Windows settings, the clipboard, the app lifetime.</summary>
public interface IShell
{
    void OpenUrl(string url);
    void OpenSoundRecordingSettings();
    Task CopyTextAsync(string text);
    void Quit();
}

public sealed class EngineStatusSource(ToggleEngine engine) : IStatusSource
{
    public IReadOnlyList<ServiceSnapshot> GetServices() => engine.GetServiceSnapshots();

    public MicrophoneDiagnosis? DiagnoseMicrophone() => engine.DiagnoseMicrophone();
}
