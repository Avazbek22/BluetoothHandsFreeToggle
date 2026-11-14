using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui.Model;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Gui.Services;

public enum StartupDialogIcon
{
    Error,
    Warning,
    Information
}

/// <summary>Plain Windows message boxes: they still work when the panel itself cannot start.</summary>
public interface IStartupDialog
{
    void Show(string title, string body, StartupDialogIcon icon);

    /// <returns><c>true</c> when the user chose Yes.</returns>
    bool Ask(string title, string body);
}

/// <summary>
/// Explains a failed start of the tray panel. While "Always stereo" is on, the headset microphone
/// stays off and the switch to turn it back on is out of reach, so the message offers to put
/// Windows back as it was. The restore runs through the same elevated worker as from the panel.
/// </summary>
public sealed class StartupRecovery(
    IStartupDialog dialog,
    IOperationRunner runner,
    Func<IReadOnlyList<ServiceSnapshot>> readServices)
{
    public const string AppTitle = "HandsFree Toggle";

    public void Report(Exception exception, string logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var title = Text.Get("gui.error.startup.title");
        var body = Text.Format("gui.error.startup.body", exception.Message, logPath);

        if (!IsHardModeActive())
        {
            dialog.Show(title, body, StartupDialogIcon.Error);
            return;
        }

        if (!dialog.Ask(title, $"{body}\n\n{Text.Get("gui.error.startup.restoreOffer")}"))
            return;

        var outcome = Restore();
        var report = outcome.Report;
        if (outcome.Kind == OperationOutcomeKind.Cancelled)
            dialog.Show(AppTitle, Text.Get("gui.uacCancelled"), StartupDialogIcon.Warning);
        else if (report is { RebootRequired: true })
            dialog.Show(Text.Get("gui.reboot.title"), Text.Get("gui.reboot.restore"), StartupDialogIcon.Warning);
        else if (report is { Success: true })
            dialog.Show(AppTitle, Text.Get("gui.error.startup.restored"), StartupDialogIcon.Information);
        else
            dialog.Show(Text.Get("gui.failed.restore"), report?.ToPrettyText() ?? outcome.Error ?? "", StartupDialogIcon.Error);
    }

    private bool IsHardModeActive()
    {
        try
        {
            return LiveStatus.Evaluate(readServices(), microphone: null).HardModeActive;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CrashLog.Write("The service state could not be read after a failed start", exception);
            return false;
        }
    }

    private OperationOutcome Restore()
    {
        try
        {
            // There is no UI thread to return to; a pool thread keeps any leftover context out of the await.
            return Task.Run(() => runner.RunAsync(OperationKind.Restore, _ => { })).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CrashLog.Write("Turning Always stereo off after a failed start failed", exception);
            return OperationOutcome.Failed(exception.Message);
        }
    }
}
